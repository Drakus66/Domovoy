using System.Collections.Concurrent;

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using MQTTnet;
using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

/// <summary>
/// Adapter for ESP32/ESP8266 boards running ESPHome with its <c>mqtt:</c> component (roadmap Epic 2J). It
/// speaks Home-Assistant MQTT Discovery: each entity announces a retained config on
/// <c>homeassistant/&lt;component&gt;/[&lt;node&gt;/]&lt;object_id&gt;/config</c>, which the adapter turns into
/// capabilities via <see cref="EspHomeCodec"/> and publishes as a single <see cref="DeviceDiscoveredV1"/> per
/// board (several entities of one board share a device id, like a Native hub). State topics are learned from
/// discovery and subscribed dynamically; commands are encoded back onto each entity's command topic; the
/// board's availability (birth/LWT) topic fans out to <see cref="DeviceOnlineChangedV1"/> for its entities.
///
/// <para>Nothing else in the stack changes — the device flows through the common capability path, so the 2D
/// classifier assigns its archetype and the WebUI/automations/ML see it like any other device.</para>
/// </summary>
public sealed class EspHomeMqttAdapter : IProtocolAdapter
{
    // Discovery prefix HA/ESPHome publish retained configs under.
    private const string DiscoveryPrefix = "homeassistant/";

    // Recommended convention (see the board yaml docs): point ESPHome's topic_prefix here so state/command
    // topics fall under one wildcard the adapter statically claims. Topics outside it are still handled —
    // they're learned from discovery and subscribed dynamically (see EnsureSubscribed).
    private const string StatePrefix = "domovoy/esphome/";

    private readonly ILogger<EspHomeMqttAdapter> _logger;
    private readonly IMessageBus _messageBus;
    private IMqttClient? _mqttClient;

    // deviceKey (board identifier) → aggregated device. One board fronts many entities as one device.
    private readonly ConcurrentDictionary<string, EspDevice> _devices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, EspDevice> _devicesById = new();

    // Fast inbound routing: state topic → the owning device + channel; availability topic → the boards on it.
    private readonly ConcurrentDictionary<string, (Guid DeviceId, EspChannel Channel)> _channelByStateTopic = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AvailBinding> _availByTopic = new(StringComparer.Ordinal);

    // Topics we've claimed (so CanHandleTopic routes them) and dynamically subscribed to (dedup).
    private readonly ConcurrentDictionary<string, byte> _ownedTopics = new(StringComparer.Ordinal);

    public string Name => "EspHome";

    public EspHomeMqttAdapter(ILogger<EspHomeMqttAdapter> logger, IMessageBus messageBus)
    {
        _logger = logger;
        _messageBus = messageBus;
    }

    public async Task StartAsync(IMqttClient mqttClient, CancellationToken token)
    {
        _mqttClient = mqttClient;

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(DiscoveryPrefix + "#")
            .WithTopicFilter(StatePrefix + "#")
            .Build();
        await _mqttClient.SubscribeAsync(options, token);

        // Capability-addressed commands from the bus. Own queue + ignores devices we don't own, like the
        // other adapters, so only the owning adapter acts.
        await _messageBus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            $"connectivity-{Name}-commands-v1",
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            HandleCapabilityCommandAsync,
            token);

        // Known caveat: RabbitMQ's MQTT plugin does not redeliver retained messages to wildcard subscriptions,
        // so after a restart the retained discovery configs may not arrive. ESPHome re-publishes discovery on
        // its own reconnect + birth message, which re-seeds us; command topics are also re-learned then.
        _logger.LogInformation("EspHomeMqttAdapter started (discovery '{Prefix}#', state convention '{State}#')",
            DiscoveryPrefix, StatePrefix);
    }

    public Task StopAsync(CancellationToken token) => Task.CompletedTask;

    public bool CanHandleTopic(string topic) =>
        topic.StartsWith(DiscoveryPrefix, StringComparison.Ordinal) ||
        topic.StartsWith(StatePrefix, StringComparison.Ordinal) ||
        _ownedTopics.ContainsKey(topic);

    public async Task HandleMessageAsync(string topic, string payload)
    {
        try
        {
            if (topic.StartsWith(DiscoveryPrefix, StringComparison.Ordinal) && topic.EndsWith("/config", StringComparison.Ordinal))
                await HandleDiscoveryAsync(topic, payload);
            else if (_channelByStateTopic.TryGetValue(topic, out var route))
                await HandleStateAsync(route.DeviceId, route.Channel, payload);
            else if (_availByTopic.TryGetValue(topic, out var avail))
                await HandleAvailabilityAsync(avail, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ESPHome message on topic {Topic}", topic);
        }
    }

    // homeassistant/<component>/[<node>/]<object_id>/config → parse component + object_id, build/merge the entity.
    private async Task HandleDiscoveryAsync(string topic, string payload)
    {
        var segments = topic.Split('/');
        if (segments.Length < 4) return; // prefix / component / object_id / config
        var component = segments[1];
        var objectId = segments[^2];

        var entity = EspHomeCodec.TryBuildEntity(component, objectId, payload);
        if (entity is null) return; // empty (config cleared) / unsupported component

        var device = _devices.GetOrAdd(entity.DeviceKey, key =>
        {
            var id = DeviceIdFactory.Derive(Name, key);
            var d = new EspDevice(id, key);
            _devicesById[id] = d;
            return d;
        });

        // Prefer the richest metadata seen across the board's entities.
        device.Name = entity.DeviceName;
        device.Manufacturer ??= entity.Manufacturer;
        device.Model ??= entity.Model;

        foreach (var channel in entity.Channels)
        {
            device.ChannelsByCap[channel.CapabilityId] = channel;
            if (channel.StateTopic is { } st)
            {
                _channelByStateTopic[st] = (device.DeviceId, channel);
                await EnsureSubscribedAsync(st);
            }
        }

        if (entity.AvailabilityTopic is { } avty)
        {
            var binding = _availByTopic.GetOrAdd(avty, _ => new AvailBinding(entity.OnlinePayload, entity.OfflinePayload));
            binding.DeviceIds.Add(device.DeviceId);
            await EnsureSubscribedAsync(avty);
        }

        await PublishDiscoveryAsync(device);
    }

    private async Task PublishDiscoveryAsync(EspDevice device)
    {
        var capabilities = device.ChannelsByCap.Values.Select(c => c.Capability).ToList();
        if (capabilities.Count == 0) return;

        var descriptor = new DeviceDescriptor(
            Id: device.DeviceId,
            Name: device.Name,
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(AdapterSource: Name, HardwareId: device.HardwareId),
            Capabilities: capabilities,
            Manufacturer: device.Manufacturer,
            Model: device.Model);

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"connectivity/{Name}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: device.DeviceId.ToString());

        await _messageBus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope);
        _logger.LogInformation("ESPHome device '{Name}' ({DeviceId}) discovered: {Count} caps [{Caps}]",
            device.Name, device.DeviceId, capabilities.Count, string.Join(", ", capabilities.Select(c => c.Id)));
    }

    private async Task HandleStateAsync(Guid deviceId, EspChannel channel, string payload)
    {
        var value = channel.Decode(payload);
        if (value is null) return;

        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"connectivity/{Name}",
            data: new DeviceStateReportV1(deviceId, new Dictionary<string, object?> { [channel.CapabilityId] = value }),
            subject: deviceId.ToString());

        await _messageBus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope);
    }

    private async Task HandleAvailabilityAsync(AvailBinding binding, string payload)
    {
        var p = payload.Trim().Trim('"');
        bool isOnline;
        if (string.Equals(p, binding.Online, StringComparison.OrdinalIgnoreCase)) isOnline = true;
        else if (string.Equals(p, binding.Offline, StringComparison.OrdinalIgnoreCase)) isOnline = false;
        else return; // unrecognized availability payload

        foreach (var deviceId in binding.DeviceIds)
        {
            var envelope = Envelope<DeviceOnlineChangedV1>.Create(
                MessageTypes.DeviceOnlineChanged,
                source: $"connectivity/{Name}",
                data: new DeviceOnlineChangedV1(deviceId, isOnline),
                subject: deviceId.ToString());

            await _messageBus.PublishAsync(BusTopology.EventsExchange, BusTopology.DeviceOnlineChangedKey, envelope);
        }
    }

    private async Task HandleCapabilityCommandAsync(Envelope<DeviceCommandV1> envelope)
    {
        var command = envelope.Data;
        if (command is null || _mqttClient is null) return;
        if (!_devicesById.TryGetValue(command.DeviceId, out var device)) return; // not ours

        foreach (var (capabilityId, value) in command.Set)
        {
            if (!device.ChannelsByCap.TryGetValue(capabilityId, out var channel)) continue;
            if (channel.CommandTopic is not { } topic || channel.Encode is null) continue;

            var payload = channel.Encode(value);
            if (payload is null) continue;

            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .Build());
            _logger.LogInformation("ESPHome command for {DeviceId} {Cap} → {Topic}", command.DeviceId, capabilityId, topic);
        }
    }

    // Claim the topic for routing and, when it falls outside the statically-subscribed wildcards, subscribe to it.
    private async Task EnsureSubscribedAsync(string topic)
    {
        if (!_ownedTopics.TryAdd(topic, 0)) return; // already owned/subscribed

        if (topic.StartsWith(DiscoveryPrefix, StringComparison.Ordinal) ||
            topic.StartsWith(StatePrefix, StringComparison.Ordinal))
            return; // already covered by a static wildcard subscription

        if (_mqttClient is null) return;
        try
        {
            await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build());
            _logger.LogDebug("ESPHome dynamically subscribed to {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ESPHome could not subscribe to learned topic {Topic}", topic);
        }
    }

    /// <summary>An ESPHome board aggregated from its entities' discovery configs.</summary>
    private sealed class EspDevice(Guid deviceId, string hardwareId)
    {
        public Guid DeviceId { get; } = deviceId;
        public string HardwareId { get; } = hardwareId;
        public string Name { get; set; } = hardwareId;
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }

        /// <summary>capability id → its latest channel (decode/encode + topics).</summary>
        public ConcurrentDictionary<string, EspChannel> ChannelsByCap { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Availability (birth/LWT) topic binding: online/offline payloads + the boards it governs.</summary>
    private sealed class AvailBinding(string online, string offline)
    {
        public string Online { get; } = online;
        public string Offline { get; } = offline;
        public HashSet<Guid> DeviceIds { get; } = new();
    }
}
