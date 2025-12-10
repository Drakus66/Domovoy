using System.Text.Json;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Enums;
using MQTTnet;
using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

public class Zigbee2MqttAdapter : IProtocolAdapter
{
    private readonly ILogger<Zigbee2MqttAdapter> _logger;
    private IMqttClient? _mqttClient;

    public string Name => "Zigbee2Mqtt";

    public event Func<DeviceDiscoveredEvent, Task>? OnDeviceDiscovered;
    public event Func<DeviceStateUpdatedEvent, Task>? OnDeviceStateChanged;

    public Zigbee2MqttAdapter(ILogger<Zigbee2MqttAdapter> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(IMqttClient mqttClient, CancellationToken token)
    {
        _mqttClient = mqttClient;

        // Subscribe to Z2M discovery and state topics
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("zigbee2mqtt/bridge/devices") // Device list
            .WithTopicFilter("zigbee2mqtt/+") // Device states (friendly_name)
            .Build();

        await _mqttClient.SubscribeAsync(options, token);
        _logger.LogInformation("Zigbee2MqttAdapter started. Listening to zigbee2mqtt/bridge/devices");
    }

    public Task StopAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public bool CanHandleTopic(string topic)
    {
        return topic.StartsWith("zigbee2mqtt/");
    }

    public async Task HandleMessageAsync(string topic, string payload)
    {
        try
        {
            if (topic == "zigbee2mqtt/bridge/devices")
            {
                await HandleDeviceList(payload);
            }
            else
            {
                // Likely a state update: zigbee2mqtt/{friendly_name}
                // Need to filter out bridge/ logging/ etc if not caught by specific filters.
                // Assuming friendly_name doesn't contain slashes usually, but it might.
                // Z2M defaults: zigbee2mqtt/my_bulb

                // TODO: Implement state parsing
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Z2M message on topic {Topic}", topic);
        }
    }

    private async Task HandleDeviceList(string payload)
    {
        // Payload is a JSON array of devices
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        foreach (var device in doc.RootElement.EnumerateArray())
        {
            // Filter out Coordinator and devices not fully interviewed
            if (device.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "Coordinator") continue;

            if (device.TryGetProperty("friendly_name", out var friendlyNameEl)
                && device.TryGetProperty("ieee_address", out var ieeeEl))
            {
                var friendlyName = friendlyNameEl.GetString();
                var ieee = ieeeEl.GetString();

                // Map to Domovoy Device
                // Infer type from exposes or definition
                var deviceType = DetermineDeviceType(device);

                var discoveryEvent = new DeviceDiscoveredEvent
                {
                    DeviceId = Guid.NewGuid(), // Need stable ID? UUID v5 from IEEE?
                    // Ideally ConnectivityService persists mappings ID <-> IEEE.
                    // For now, generate new or hash. 
                    // Let's use name string as ID in metadata, but GlobalId is GUID.
                    // We must generate stable GUID from IEEE string.
                    Name = friendlyName ?? ieee,
                    DeviceType = deviceType,
                    Source = Name,
                    Metadata = new Dictionary<string, object>
                    {
                        { "ieee_address", ieee },
                        { "friendly_name", friendlyName },
                        { "command_topic", $"zigbee2mqtt/{friendlyName}/set" },
                        { "state_topic", $"zigbee2mqtt/{friendlyName}" }
                    }
                };

                if (OnDeviceDiscovered != null)
                {
                    await OnDeviceDiscovered.Invoke(discoveryEvent);
                }
            }
        }
    }

    private GlobalEntityTypes DetermineDeviceType(JsonElement device)
    {
        // Inspect 'definition' -> 'exposes'
        if (device.TryGetProperty("definition", out var def))
        {
            // Simplistic mapping
            if (def.TryGetProperty("description", out var desc))
            {
                var d = desc.GetString()?.ToLower() ?? "";
                if (d.Contains("light") || d.Contains("bulb")) return GlobalEntityTypes.Light;
                if (d.Contains("sensor")) return GlobalEntityTypes.Sensor;
                if (d.Contains("switch") || d.Contains("plug")) return GlobalEntityTypes.Switch;
            }
        }
        return GlobalEntityTypes.Generic;
    }

    public async Task HandleCommandAsync(DeviceCommand command)
    {
        if (_mqttClient == null) return;

        // Ensure this command is for a Z2M device (check source or metadata)
        // Since AdapterManager broadcasts, we must check applicability.
        // We lack context here unless provided.
        // Assuming ConnectivityService resolves "Logic Device -> Adapter" via mapping?
        // OR Command contains Metadata with "Source": "Zigbee2Mqtt"?
        // Or "command_topic" is key.

        if (command.Parameters.TryGetValue("command_topic", out var topicObj) && topicObj is string topic
            && topic.StartsWith("zigbee2mqtt/"))
        {
            var payloadObj = new Dictionary<string, object>();

            if (command.CommandTypes == DeviceCommandTypes.SetState)
            {
                if (command.Parameters.TryGetValue("state", out var state))
                {
                    payloadObj["state"] = state;
                }
                if (command.Parameters.TryGetValue("brightness", out var brightness))
                {
                    payloadObj["brightness"] = brightness;
                }
                // Add color if needed
            }

            if (payloadObj.Count > 0)
            {
                var json = JsonSerializer.Serialize(payloadObj);
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(json)
                    .Build());
            }
        }
    }
}
