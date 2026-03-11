using System.Text.Json;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using MQTTnet;
using MQTTnet.Client;

namespace Domovoy.Connectivity.Adapters;

public class DomovoyNativeAdapter : IProtocolAdapter
{
    private readonly ILogger<DomovoyNativeAdapter> _logger;
    private IMqttClient? _mqttClient;

    public string Name => "DomovoyNative";

    public event Func<DeviceDiscoveredEvent, Task>? OnDeviceDiscovered;
    public event Func<AdapterStateReportedEvent, Task>? OnAdapterStateReported;

    public DomovoyNativeAdapter(ILogger<DomovoyNativeAdapter> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(IMqttClient mqttClient, CancellationToken token)
    {
        _mqttClient = mqttClient;

        // Subscribe to Domovoy Native Discovery
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("domovoy/discovery/+/+/announce") // standard pattern
            .WithTopicFilter("domovoy/discovery/#") // catch-all for now
            .Build();

        await _mqttClient.SubscribeAsync(options, token);
        _logger.LogInformation("DomovoyNativeAdapter started and subscribed.");
    }

    public Task StopAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public bool CanHandleTopic(string topic)
    {
        return topic.StartsWith("domovoy/discovery/");
    }

    public async Task HandleMessageAsync(string topic, string payload)
    {
        try
        {
            var announcement = JsonSerializer.Deserialize<DeviceDiscoveredEvent>(payload);
            if (announcement != null && OnDeviceDiscovered != null)
            {
                // Validate or enrich if needed
                announcement.Source = Name;
                await OnDeviceDiscovered.Invoke(announcement);
                _logger.LogInformation("Processed Domovoy Native discovery for {DeviceId}", announcement.DeviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Domovoy Native payload");
        }
    }

    public async Task HandleCommandAsync(DeviceCommand command)
    {
        if (_mqttClient == null) return;

        // Domovoy Native Command topic convention: domovoy/command/{type}/{id}
        // Needs metadata to know the topic, or standardized convention.
        // Assuming we store topic in Metadata or use convention.
        // Let's rely on Metadata["command_topic"] if present, or generic convention.

        if (command.Parameters.TryGetValue("command_topic", out var topicObj) && topicObj is string topic)
        {
            // Raw payload? Or JSON?
            // Native protocol usually expects JSON or simple values.
            // We'll publish the Command Action/Params as JSON.

            var payload = JsonSerializer.Serialize(new { action = command.CommandTypes.ToString(), @params = command.Parameters });

            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
               .WithTopic(topic)
               .WithPayload(payload)
               .Build());
        }
    }
}
