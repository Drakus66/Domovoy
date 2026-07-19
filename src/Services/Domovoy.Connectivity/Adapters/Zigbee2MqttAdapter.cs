// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;
using System.Text.Json;
using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Connectivity.Services;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;
using MQTTnet;
using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

public class Zigbee2MqttAdapter : IProtocolAdapter
{
    private readonly ILogger<Zigbee2MqttAdapter> _logger;
    private readonly ZigbeeBridgeCache _cache;
    private readonly IMessageBus _messageBus;
    private IMqttClient? _mqttClient;

    // Per-device capability binding (z2m exposes → capabilities + codec). Indexed by friendly name
    // (for inbound state decode) and by deterministic device id (for outbound command encode).
    // Rebuilt on every bridge/devices publish, so it self-heals after restarts.
    private readonly ConcurrentDictionary<string, Z2mBinding> _bindingsByFriendly = new();
    private readonly ConcurrentDictionary<Guid, Z2mBinding> _bindingsById = new();

    // True once z2m's retained bridge/state has been received at least once. Gates the periodic
    // re-publish so we don't emit an all-empty bridge snapshot before z2m has reported anything.
    private volatile bool _bridgeStateReceived;

    // How often the cached bridge state/info is re-emitted on the bus (see RepublishBridgeStateAsync).
    private static readonly TimeSpan BridgeRepublishInterval = TimeSpan.FromSeconds(15);

    public string Name => "Zigbee2Mqtt";

    public Zigbee2MqttAdapter(
        ILogger<Zigbee2MqttAdapter> logger,
        ZigbeeBridgeCache cache,
        IMessageBus messageBus)
    {
        _logger = logger;
        _cache = cache;
        _messageBus = messageBus;
    }

    public async Task StartAsync(IMqttClient mqttClient, CancellationToken token)
    {
        _mqttClient = mqttClient;

        await SubscribeAsync(mqttClient, token);

        await _messageBus.SubscribeAsync<ZigbeeBridgeCommand>(
            MessageBusConfiguration.ZigbeeBridgeCommandsQueue,
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeCommandRoutingKey,
            HandleBridgeCommandAsync,
            token);

        // Capability-addressed commands (roadmap Step 3). Each adapter uses its own queue and
        // ignores commands for devices it doesn't own, so only the owning adapter acts.
        await _messageBus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            $"connectivity-{Name}-commands-v1",
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            HandleCapabilityCommandAsync,
            token);

        // z2m publishes bridge/state and bridge/info as one-shot retained MQTT messages, which we
        // forward to the bus exactly once. The bus events are NOT retained, so any consumer that
        // (re)starts later — e.g. ApiGateway — misses them and shows the bridge as permanently
        // offline. Periodically re-emitting the cached snapshot lets late subscribers converge.
        _ = Task.Run(() => RepublishBridgeStateAsync(token), token);

        _logger.LogInformation("Zigbee2MqttAdapter started");
    }

    public Task StopAsync(CancellationToken token) => Task.CompletedTask;

    /// <summary>(Re)subscribes the z2m MQTT topics; re-run on every reconnect (clean session loses subs).</summary>
    public async Task SubscribeAsync(IMqttClient mqttClient, CancellationToken token)
    {
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("zigbee2mqtt/bridge/state")
            .WithTopicFilter("zigbee2mqtt/bridge/info")
            .WithTopicFilter("zigbee2mqtt/bridge/devices")
            .WithTopicFilter("zigbee2mqtt/bridge/event")
            .WithTopicFilter("zigbee2mqtt/bridge/response/#")
            .WithTopicFilter("zigbee2mqtt/+")
            // Per-device availability (z2m 'availability:' feature). This is the canonical per-device
            // liveness signal — z2m marks an individual device offline when it stops responding (coordinator
            // lost, device unplugged) even while the bridge itself stays online, which the bridge-down sweep
            // alone would miss. Retained, so a (re)subscribe re-delivers the last known state.
            .WithTopicFilter("zigbee2mqtt/+/availability")
            .Build();

        await mqttClient.SubscribeAsync(options, token);
    }

    public bool CanHandleTopic(string topic) => topic.StartsWith("zigbee2mqtt/");

    public async Task HandleMessageAsync(string topic, string payload)
    {
        try
        {
            switch (topic)
            {
                case "zigbee2mqtt/bridge/state":
                    await HandleBridgeState(payload);
                    break;
                case "zigbee2mqtt/bridge/info":
                    await HandleBridgeInfo(payload);
                    break;
                case "zigbee2mqtt/bridge/devices":
                    await HandleDeviceList(payload);
                    break;
                case "zigbee2mqtt/bridge/event":
                    await HandleBridgeEvent(payload);
                    break;
                default:
                    if (topic.StartsWith("zigbee2mqtt/bridge/response/"))
                        break;
                    if (FriendlyNameFromAvailabilityTopic(topic) is { } availabilityFriendly)
                        await HandleDeviceAvailability(availabilityFriendly, payload);
                    else
                        await HandleDeviceState(topic, payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Z2M message on topic {Topic}", topic);
        }
    }

    private async Task HandleBridgeState(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var stateStr = doc.RootElement.TryGetProperty("state", out var s)
            ? s.GetString()
            : doc.RootElement.GetString();
        var isOnline = stateStr == "online";

        _cache.UpdateBridgeState(isOnline);
        _bridgeStateReceived = true;

        var ev = new ZigbeeBridgeStateEvent { IsOnline = isOnline, Source = Name };
        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeStateRoutingKey,
            ev);

        _logger.LogInformation("Zigbee bridge is {State}", isOnline ? "ONLINE" : "OFFLINE");
    }

    /// <summary>
    /// Periodically re-publishes the cached bridge state and info so consumers that join the bus
    /// after the initial retained MQTT publish (e.g. a restarted ApiGateway) converge to the real
    /// state. Without this the bus events are one-shot and a late subscriber shows the bridge as
    /// offline indefinitely.
    /// </summary>
    private async Task RepublishBridgeStateAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(BridgeRepublishInterval, token);

                if (!_bridgeStateReceived) continue; // nothing received from z2m yet

                var info = _cache.GetBridgeInfo();

                await _messageBus.PublishAsync(
                    MessageBusConfiguration.ZigbeeBridgeExchange,
                    MessageBusConfiguration.ZigbeeBridgeStateRoutingKey,
                    new ZigbeeBridgeStateEvent { IsOnline = info.IsOnline, Source = Name });

                await _messageBus.PublishAsync(
                    MessageBusConfiguration.ZigbeeBridgeExchange,
                    MessageBusConfiguration.ZigbeeBridgeInfoRoutingKey,
                    new ZigbeeBridgeInfoEvent
                    {
                        Version = info.Version,
                        CoordinatorType = info.CoordinatorType,
                        CoordinatorAddress = info.CoordinatorAddress,
                        Channel = info.Channel,
                        PanId = info.PanId,
                        PermitJoin = info.PermitJoin,
                        PermitJoinTimeout = info.PermitJoinTimeout,
                        Source = Name,
                    });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-publish cached Zigbee bridge state");
            }
        }
    }

    private async Task HandleBridgeInfo(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var info = new ZigbeeBridgeInfo
        {
            Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            PermitJoin = root.TryGetProperty("permit_join", out var pj) && pj.GetBoolean(),
            PermitJoinTimeout = root.TryGetProperty("permit_join_timeout", out var pjt) ? pjt.GetInt32() : 0,
        };

        if (root.TryGetProperty("coordinator", out var coord))
        {
            info.CoordinatorType = coord.TryGetProperty("type", out var ct) ? ct.GetString() ?? "" : "";
            info.CoordinatorAddress = coord.TryGetProperty("ieee_address", out var ca) ? ca.GetString() ?? "" : "";
        }

        if (root.TryGetProperty("network", out var net))
        {
            info.Channel = net.TryGetProperty("channel", out var ch) ? ch.GetInt32() : 0;
            info.PanId = net.TryGetProperty("pan_id", out var pan) ? pan.GetInt32() : 0;
        }

        _cache.UpdateBridgeInfo(info);

        var ev = new ZigbeeBridgeInfoEvent
        {
            Version = info.Version,
            CoordinatorType = info.CoordinatorType,
            CoordinatorAddress = info.CoordinatorAddress,
            Channel = info.Channel,
            PanId = info.PanId,
            PermitJoin = info.PermitJoin,
            PermitJoinTimeout = info.PermitJoinTimeout,
            Source = Name,
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeInfoRoutingKey,
            ev);
    }

    private async Task HandleBridgeEvent(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var eventType = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var data = root.TryGetProperty("data", out var d) ? d : default;

        var friendly = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("friendly_name", out var fn)
            ? fn.GetString() ?? ""
            : "";
        var ieee = data.ValueKind != JsonValueKind.Undefined && data.TryGetProperty("ieee_address", out var ia)
            ? ia.GetString() ?? ""
            : "";

        var ev = new ZigbeeNetworkEvent
        {
            EventType = eventType,
            FriendlyName = friendly,
            IeeeAddress = ieee,
            Source = Name,
        };

        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeNetworkEventRoutingKey,
            ev);

        _logger.LogInformation("Zigbee network event: {Type} device={FriendlyName}", eventType, friendly);
    }

    private async Task HandleDeviceList(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        var cacheDevices = new List<ZigbeeDeviceInfo>();

        foreach (var device in doc.RootElement.EnumerateArray())
        {
            if (device.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "Coordinator") continue;

            if (!device.TryGetProperty("friendly_name", out var friendlyNameEl) ||
                !device.TryGetProperty("ieee_address", out var ieeeEl))
                continue;

            var friendlyName = friendlyNameEl.GetString() ?? "";
            var ieee = ieeeEl.GetString() ?? "";

            var interviewCompleted = !device.TryGetProperty("interview_completed", out var ic) || ic.GetBoolean();

            var model = "";
            var vendor = "";
            var description = "";
            if (device.TryGetProperty("definition", out var def))
            {
                model = def.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
                vendor = def.TryGetProperty("vendor", out var vn) ? vn.GetString() ?? "" : "";
                description = def.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : "";
            }

            cacheDevices.Add(new ZigbeeDeviceInfo
            {
                IeeeAddress = ieee,
                FriendlyName = friendlyName,
                Type = device.TryGetProperty("type", out var dt) ? dt.GetString() ?? "" : "",
                Supported = device.TryGetProperty("supported", out var sup) && sup.GetBoolean(),
                Model = model,
                Vendor = vendor,
                Description = description,
                InterviewCompleted = interviewCompleted,
            });

            if (!interviewCompleted) continue;

            await PublishCapabilityDiscoveryAsync(device, friendlyName, ieee, model, vendor);
        }

        _cache.UpdateDevices(cacheDevices);
    }

    private async Task HandleDeviceState(string topic, string payload)
    {
        var friendlyName = topic.Replace("zigbee2mqtt/", "");

        try
        {
            var state = JsonSerializer.Deserialize<Dictionary<string, object>>(payload);
            if (state != null)
                _cache.UpdateDeviceState(friendlyName, state);
        }
        catch { /* non-JSON payloads are fine to ignore */ }

        await PublishNormalizedStateAsync(friendlyName, payload);
    }

    /// <summary>
    /// Handles a per-device availability message (<c>zigbee2mqtt/&lt;friendly&gt;/availability</c>, z2m
    /// 'availability:' feature). Emits <see cref="DeviceOnlineChangedV1"/> on the canonical bus topology —
    /// exactly like the ESPHome/Native adapters — so a device that stops responding while the bridge stays
    /// up (coordinator lost, device unplugged/out of range) is marked offline in the read-model, not left
    /// stuck online. The device flips back online on the next <c>online</c> availability or state report.
    /// Ignores devices not yet discovered (no capability binding → no deterministic id to address).
    /// </summary>
    private async Task HandleDeviceAvailability(string friendlyName, string payload)
    {
        var isOnline = ParseAvailability(payload);
        if (isOnline is null) return; // unrecognized payload

        if (!_bindingsByFriendly.TryGetValue(friendlyName, out var binding)) return; // not discovered yet

        var envelope = Envelope<DeviceOnlineChangedV1>.Create(
            MessageTypes.DeviceOnlineChanged,
            source: $"connectivity/{Name}",
            data: new DeviceOnlineChangedV1(binding.DeviceId, isOnline.Value),
            subject: binding.DeviceId.ToString());

        await _messageBus.PublishAsync(BusTopology.EventsExchange, BusTopology.DeviceOnlineChangedKey, envelope);

        _logger.LogInformation(
            "Zigbee device '{Friendly}' is {State}", friendlyName, isOnline.Value ? "ONLINE" : "OFFLINE");
    }

    /// <summary>
    /// Extracts the friendly name from an availability topic <c>zigbee2mqtt/&lt;friendly&gt;/availability</c>,
    /// or <c>null</c> if the topic is not a (single-level) availability topic. Mirrors the adapter's existing
    /// single-level friendly-name assumption (<c>zigbee2mqtt/+</c> for state).
    /// </summary>
    public static string? FriendlyNameFromAvailabilityTopic(string topic)
    {
        const string prefix = "zigbee2mqtt/";
        const string suffix = "/availability";
        if (topic.Length <= prefix.Length + suffix.Length) return null;
        if (!topic.StartsWith(prefix, StringComparison.Ordinal) ||
            !topic.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var friendly = topic[prefix.Length..^suffix.Length];
        return string.IsNullOrEmpty(friendly) ? null : friendly;
    }

    /// <summary>
    /// Parses a z2m availability payload to online/offline, or <c>null</c> if unrecognized. Handles both the
    /// modern JSON form (<c>{"state":"online"}</c>) and the legacy bare string (<c>online</c>/<c>offline</c>).
    /// </summary>
    public static bool? ParseAvailability(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        var trimmed = payload.Trim();

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String)
                    return ToOnline(s.GetString());
            }
            catch (JsonException) { return null; }
            return null;
        }

        return ToOnline(trimmed.Trim('"'));
    }

    private static bool? ToOnline(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "online" => true,
        "offline" => false,
        _ => null,
    };

    /// <summary>
    /// Builds the capability descriptor from <c>definition.exposes</c> and publishes
    /// <see cref="DeviceDiscoveredV1"/> on the canonical bus topology. Best-effort: never throws into
    /// the legacy discovery path.
    /// </summary>
    private async Task PublishCapabilityDiscoveryAsync(JsonElement device, string friendlyName, string ieee, string model, string vendor)
    {
        try
        {
            if (string.IsNullOrEmpty(ieee)) return;
            if (!device.TryGetProperty("definition", out var definition) || definition.ValueKind != JsonValueKind.Object)
                return;

            var deviceModel = Zigbee2MqttCodec.BuildModel(definition);
            if (deviceModel.Capabilities.Count == 0) return;

            var deviceId = DeviceIdFactory.Derive(Name, ieee);
            var binding = new Z2mBinding(deviceId, friendlyName, $"zigbee2mqtt/{friendlyName}/set", deviceModel);
            _bindingsByFriendly[friendlyName] = binding;
            _bindingsById[deviceId] = binding;

            var descriptor = new DeviceDescriptor(
                Id: deviceId,
                Name: friendlyName,
                ZoneId: Guid.Empty,
                Identity: new DeviceIdentity(
                    AdapterSource: Name,
                    HardwareId: ieee,
                    StateTopic: $"zigbee2mqtt/{friendlyName}",
                    CommandTopic: $"zigbee2mqtt/{friendlyName}/set"),
                Capabilities: deviceModel.Capabilities,
                Manufacturer: string.IsNullOrEmpty(vendor) ? null : vendor,
                Model: string.IsNullOrEmpty(model) ? null : model);

            var envelope = Envelope<DeviceDiscoveredV1>.Create(
                MessageTypes.DeviceDiscovered,
                source: $"connectivity/{Name}",
                data: new DeviceDiscoveredV1(descriptor),
                subject: deviceId.ToString());

            await _messageBus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope);

            _logger.LogInformation(
                "Published capability discovery for '{Friendly}' ({Count} caps: {Caps}) as {DeviceId}",
                friendlyName, deviceModel.Capabilities.Count,
                string.Join(", ", deviceModel.Capabilities.Select(c => c.Id)), deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish capability discovery for '{Friendly}'", friendlyName);
        }
    }

    /// <summary>
    /// Decodes a z2m state payload into normalized capability values and publishes
    /// <see cref="DeviceStateReportV1"/>. Best-effort: never throws into the legacy state path.
    /// </summary>
    private async Task PublishNormalizedStateAsync(string friendlyName, string payload)
    {
        if (!_bindingsByFriendly.TryGetValue(friendlyName, out var binding)) return;

        try
        {
            var capabilityState = Zigbee2MqttCodec.Decode(binding.Model, payload);
            if (capabilityState.Values.Count == 0) return;

            var envelope = Envelope<DeviceStateReportV1>.Create(
                MessageTypes.DeviceState,
                source: $"connectivity/{Name}",
                data: new DeviceStateReportV1(binding.DeviceId, capabilityState.Values),
                subject: binding.DeviceId.ToString());

            await _messageBus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish normalized state for '{Friendly}'", friendlyName);
        }
    }

    private async Task HandleBridgeCommandAsync(ZigbeeBridgeCommand command)
    {
        if (_mqttClient == null) return;

        switch (command.CommandType)
        {
            case ZigbeeBridgeCommandType.PermitJoin:
                await PublishMqttAsync("zigbee2mqtt/bridge/request/permit_join",
                    new Dictionary<string, object> { { "time", command.PermitJoinDuration } });
                break;

            case ZigbeeBridgeCommandType.RenameDevice:
                await PublishMqttAsync("zigbee2mqtt/bridge/request/device/rename",
                    new Dictionary<string, object>
                    {
                        { "from", command.TargetDevice },
                        { "to", command.NewName }
                    });
                break;

            case ZigbeeBridgeCommandType.RemoveDevice:
                await PublishMqttAsync("zigbee2mqtt/bridge/request/device/remove",
                    new Dictionary<string, object> { { "id", command.TargetDevice } });
                break;
        }

        _logger.LogInformation("Z2M Bridge command sent: {Type}", command.CommandType);
    }

    private async Task PublishMqttAsync(string topic, Dictionary<string, object> payload)
    {
        var json = JsonSerializer.Serialize(payload);
        await _mqttClient!.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .Build());
        _logger.LogDebug("Z2M → {Topic}: {Payload}", topic, json);
    }

    /// <summary>
    /// Handles a capability-addressed command (roadmap Step 3). Encodes the normalized capability set
    /// into a z2m payload via the device's codec and publishes it to the device's command topic.
    /// Ignores devices this adapter does not own.
    /// </summary>
    private async Task HandleCapabilityCommandAsync(Envelope<DeviceCommandV1> envelope)
    {
        var command = envelope.Data;
        if (command is null || _mqttClient is null) return;
        if (!_bindingsById.TryGetValue(command.DeviceId, out var binding)) return; // not ours

        var payload = Zigbee2MqttCodec.Encode(binding.Model, command.Set);
        if (payload.Count == 0)
        {
            _logger.LogWarning("Z2M: capability command for {DeviceId} produced no z2m payload (set: {Keys})",
                command.DeviceId, string.Join(", ", command.Set.Keys));
            return;
        }

        await PublishMqttAsync(binding.CommandTopic, payload);
        _logger.LogInformation("Z2M: encoded capability command for {DeviceId} → {Topic}", command.DeviceId, binding.CommandTopic);
    }

    /// <summary>Capability binding for one Zigbee device: deterministic id, friendly name, command topic and codec model.</summary>
    private sealed record Z2mBinding(Guid DeviceId, string FriendlyName, string CommandTopic, Zigbee2MqttDeviceModel Model);
}
