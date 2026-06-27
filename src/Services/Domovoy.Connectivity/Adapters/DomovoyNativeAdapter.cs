using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Native;
using Domovoy.MessageBus;

using MQTTnet;
using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

/// <summary>
/// Adapter for native Domovoy devices — DIY firmware on Arduino/microcontrollers (see <c>Arduino/</c>),
/// communicating over the <see cref="NativeProtocol"/>. The native protocol is capability-native
/// (devices speak normalized capability values), so this adapter is near-identity: it maps native
/// announce/state/availability/command directly onto the capability contract with no value scaling.
/// </summary>
public class DomovoyNativeAdapter : IProtocolAdapter
{
    // Native announce parsing tolerates string enums and case-insensitive names (firmware-friendly).
    private static readonly JsonSerializerOptions AnnounceOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<DomovoyNativeAdapter> _logger;
    private readonly IMessageBus _messageBus;
    private IMqttClient? _mqttClient;

    // Logical device id -> hardware id, to build the device's /set topic when a command arrives.
    private readonly ConcurrentDictionary<Guid, string> _hwIdByDeviceId = new();

    // hubId -> hardware ids it fronts, so a hub Last-Will can offline every device on that board.
    // (MQTT permits one will per connection, so the board wills its hub topic, not each device.)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _hwIdsByHub = new();

    public string Name => NativeProtocol.AdapterSource;

    public DomovoyNativeAdapter(ILogger<DomovoyNativeAdapter> logger, IMessageBus messageBus)
    {
        _logger = logger;
        _messageBus = messageBus;
    }

    public async Task StartAsync(IMqttClient mqttClient, CancellationToken token)
    {
        _mqttClient = mqttClient;

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(NativeProtocol.AnnounceFilter)
            .WithTopicFilter(NativeProtocol.StateFilter)
            .WithTopicFilter(NativeProtocol.AvailabilityFilter)
            .WithTopicFilter(NativeProtocol.HubStatusFilter)
            .Build();
        await _mqttClient.SubscribeAsync(options, token);

        // Capability-addressed commands from the bus. Filters to devices this adapter owns.
        await _messageBus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            $"connectivity-{Name}-commands-v1",
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            HandleCapabilityCommandAsync,
            token);

        // Ask all live devices to re-announce so we relearn deviceId→hardwareId after a (re)start.
        // RabbitMQ's MQTT plugin does not redeliver retained announces to our wildcard subscription,
        // so without this prompt commands to already-connected devices would be silently undeliverable.
        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.DiscoverTopic)
            .Build(), token);

        _logger.LogInformation("DomovoyNativeAdapter started (protocol root '{Root}/'), discovery requested", NativeProtocol.Root);
    }

    public Task StopAsync(CancellationToken token) => Task.CompletedTask;

    public bool CanHandleTopic(string topic) =>
        topic.StartsWith(NativeProtocol.Root + "/", StringComparison.Ordinal) ||
        topic.StartsWith(NativeProtocol.HubRoot + "/", StringComparison.Ordinal);

    public async Task HandleMessageAsync(string topic, string payload)
    {
        try
        {
            if (topic.StartsWith(NativeProtocol.HubRoot + "/", StringComparison.Ordinal))
            {
                if (topic.EndsWith("/status", StringComparison.Ordinal))
                    await HandleHubStatusAsync(topic, payload);
            }
            else if (topic.EndsWith("/announce", StringComparison.Ordinal))
                await HandleAnnounceAsync(payload);
            else if (topic.EndsWith("/state", StringComparison.Ordinal))
                await HandleStateAsync(topic, payload);
            else if (topic.EndsWith("/availability", StringComparison.Ordinal))
                await HandleAvailabilityAsync(topic, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling native message on topic {Topic}", topic);
        }
    }

    private async Task HandleAnnounceAsync(string payload)
    {
        var announce = JsonSerializer.Deserialize<NativeAnnounceV1>(payload, AnnounceOptions);
        if (announce is null || string.IsNullOrWhiteSpace(announce.DeviceId))
        {
            _logger.LogWarning("Native announce ignored: empty/invalid payload");
            return;
        }

        var deviceId = DeviceIdFactory.Derive(Name, announce.DeviceId);
        _hwIdByDeviceId[deviceId] = announce.DeviceId;
        if (!string.IsNullOrWhiteSpace(announce.Hub))
            _hwIdsByHub.GetOrAdd(announce.Hub, _ => new()).TryAdd(announce.DeviceId, 0);

        var descriptor = new DeviceDescriptor(
            Id: deviceId,
            Name: announce.Name,
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(
                AdapterSource: Name,
                HardwareId: announce.DeviceId,
                StateTopic: NativeProtocol.StateTopic(announce.DeviceId),
                CommandTopic: NativeProtocol.SetTopic(announce.DeviceId)),
            Capabilities: announce.Capabilities,
            Manufacturer: null,
            Model: announce.Model);

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"connectivity/{Name}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: deviceId.ToString());

        await _messageBus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope);
        _logger.LogInformation("Native device announced: '{Name}' ({DeviceId}) caps=[{Caps}]",
            announce.Name, deviceId, string.Join(", ", announce.Capabilities.Select(c => c.Id)));
    }

    private async Task HandleStateAsync(string topic, string payload)
    {
        var hwId = NativeProtocol.DeviceIdFromTopic(topic);
        if (string.IsNullOrEmpty(hwId)) return;

        var state = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload);
        if (state is null || state.Count == 0) return;

        var deviceId = DeviceIdFactory.Derive(Name, hwId);

        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"connectivity/{Name}",
            data: new DeviceStateReportV1(deviceId, state),
            subject: deviceId.ToString());

        await _messageBus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope);
    }

    private async Task HandleAvailabilityAsync(string topic, string payload)
    {
        var hwId = NativeProtocol.DeviceIdFromTopic(topic);
        if (string.IsNullOrEmpty(hwId)) return;

        var isOnline = string.Equals(payload?.Trim().Trim('"'), NativeProtocol.Online, StringComparison.OrdinalIgnoreCase);
        var deviceId = DeviceIdFactory.Derive(Name, hwId);

        var envelope = Envelope<DeviceOnlineChangedV1>.Create(
            MessageTypes.DeviceOnlineChanged,
            source: $"connectivity/{Name}",
            data: new DeviceOnlineChangedV1(deviceId, isOnline),
            subject: deviceId.ToString());

        await _messageBus.PublishAsync(BusTopology.EventsExchange, BusTopology.DeviceOnlineChangedKey, envelope);
    }

    private async Task HandleHubStatusAsync(string topic, string payload)
    {
        var hubId = NativeProtocol.HubIdFromStatusTopic(topic);
        if (string.IsNullOrEmpty(hubId)) return;

        var isOnline = string.Equals(payload?.Trim().Trim('"'), NativeProtocol.Online, StringComparison.OrdinalIgnoreCase);
        // Recovery is driven by each device re-announcing (which republishes its own availability=online),
        // so we only act on the board dropping — fan the offline out to every device the hub fronts.
        if (isOnline) return;

        if (!_hwIdsByHub.TryGetValue(hubId, out var hwIds) || hwIds.IsEmpty)
        {
            _logger.LogWarning("Hub '{Hub}' went offline but no devices are mapped to it yet", hubId);
            return;
        }

        foreach (var hwId in hwIds.Keys)
        {
            var deviceId = DeviceIdFactory.Derive(Name, hwId);
            var envelope = Envelope<DeviceOnlineChangedV1>.Create(
                MessageTypes.DeviceOnlineChanged,
                source: $"connectivity/{Name}",
                data: new DeviceOnlineChangedV1(deviceId, false),
                subject: deviceId.ToString());

            await _messageBus.PublishAsync(BusTopology.EventsExchange, BusTopology.DeviceOnlineChangedKey, envelope);
        }

        _logger.LogInformation("Hub '{Hub}' offline → marked {Count} device(s) offline", hubId, hwIds.Count);
    }

    private async Task HandleCapabilityCommandAsync(Envelope<DeviceCommandV1> envelope)
    {
        var command = envelope.Data;
        if (command is null || _mqttClient is null) return;
        if (!_hwIdByDeviceId.TryGetValue(command.DeviceId, out var hwId))
        {
            // Either the device belongs to another adapter, or we have not seen its announce yet
            // (e.g. it connected before us and RabbitMQ withheld the retained announce). Re-request
            // discovery so a live device re-announces and the next command can be routed.
            _logger.LogWarning("Native command for unknown device {DeviceId} dropped; requesting re-announce", command.DeviceId);
            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(NativeProtocol.DiscoverTopic)
                .Build());
            return;
        }

        var json = JsonSerializer.Serialize(command.Set);
        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.SetTopic(hwId))
            .WithPayload(json)
            .Build());

        _logger.LogInformation("Native command for {DeviceId} → {Topic}", command.DeviceId, NativeProtocol.SetTopic(hwId));
    }

}
