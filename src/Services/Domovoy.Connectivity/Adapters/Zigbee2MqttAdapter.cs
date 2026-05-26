using System.Text.Json;
using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Enums;
using Domovoy.Connectivity.Services;
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

    public string Name => "Zigbee2Mqtt";

    public event Func<DeviceDiscoveredEvent, Task>? OnDeviceDiscovered;
    public event Func<AdapterStateReportedEvent, Task>? OnAdapterStateReported;

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

        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("zigbee2mqtt/bridge/state")
            .WithTopicFilter("zigbee2mqtt/bridge/info")
            .WithTopicFilter("zigbee2mqtt/bridge/devices")
            .WithTopicFilter("zigbee2mqtt/bridge/event")
            .WithTopicFilter("zigbee2mqtt/bridge/response/#")
            .WithTopicFilter("zigbee2mqtt/+")
            .Build();

        await _mqttClient.SubscribeAsync(options, token);

        await _messageBus.SubscribeAsync<ZigbeeBridgeCommand>(
            MessageBusConfiguration.ZigbeeBridgeCommandsQueue,
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeCommandRoutingKey,
            HandleBridgeCommandAsync,
            token);

        _logger.LogInformation("Zigbee2MqttAdapter started");
    }

    public Task StopAsync(CancellationToken token) => Task.CompletedTask;

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

        var ev = new ZigbeeBridgeStateEvent { IsOnline = isOnline, Source = Name };
        await _messageBus.PublishAsync(
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeStateRoutingKey,
            ev);

        _logger.LogInformation("Zigbee bridge is {State}", isOnline ? "ONLINE" : "OFFLINE");
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

            var deviceType = DetermineDeviceType(device);
            var discoveryEvent = new DeviceDiscoveredEvent
            {
                DeviceId = Guid.NewGuid(),
                Name = friendlyName,
                DeviceType = deviceType,
                Source = Name,
                Metadata = new Dictionary<string, object>
                {
                    { "ieee_address", ieee },
                    { "friendly_name", friendlyName },
                    { "command_topic", $"zigbee2mqtt/{friendlyName}/set" },
                    { "state_topic", $"zigbee2mqtt/{friendlyName}" },
                    { "model", model },
                    { "vendor", vendor },
                }
            };

            if (OnDeviceDiscovered != null)
                await OnDeviceDiscovered.Invoke(discoveryEvent);
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

        if (OnAdapterStateReported != null)
        {
            await OnAdapterStateReported.Invoke(new AdapterStateReportedEvent
            {
                AdapterSource = Name,
                Topic = topic,
                Payload = payload
            });
        }
    }

    private static GlobalEntityTypes DetermineDeviceType(JsonElement device)
    {
        if (device.TryGetProperty("definition", out var def) &&
            def.TryGetProperty("description", out var desc))
        {
            var d = desc.GetString()?.ToLower() ?? "";
            if (d.Contains("light") || d.Contains("bulb")) return GlobalEntityTypes.Light;
            if (d.Contains("sensor")) return GlobalEntityTypes.Sensor;
            if (d.Contains("switch") || d.Contains("plug")) return GlobalEntityTypes.Switch;
        }
        return GlobalEntityTypes.Generic;
    }

    // =========================================================
    // Outgoing commands
    // =========================================================

    public async Task HandleCommandAsync(DeviceCommand command)
    {
        if (_mqttClient == null) return;

        if (!command.Parameters.TryGetValue("command_topic", out var topicObj) || topicObj is not string topic)
        {
            _logger.LogWarning("Z2M: command for {DeviceId} has no 'command_topic'", command.DeviceId);
            return;
        }

        if (!IsValidZigbee2MqttTopic(topic)) return;

        var payload = new Dictionary<string, object>();
        if (command.CommandTypes == DeviceCommandTypes.SetState)
        {
            AddIfPresent(payload, command.Parameters, "state");
            AddIfPresent(payload, command.Parameters, "brightness");
            AddIfPresent(payload, command.Parameters, "color_temp");
            AddIfPresent(payload, command.Parameters, "color");
            AddIfPresent(payload, command.Parameters, "transition");
        }

        if (payload.Count == 0) return;

        await PublishMqttAsync(topic, payload);
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

    private static bool IsValidZigbee2MqttTopic(string topic) => topic.StartsWith("zigbee2mqtt/");

    private static void AddIfPresent(Dictionary<string, object> target, Dictionary<string, object> source, string key)
    {
        if (source.TryGetValue(key, out var value))
            target[key] = value;
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
}
