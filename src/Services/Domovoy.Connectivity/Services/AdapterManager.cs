using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Connectivity.Adapters;
using Domovoy.MessageBus;
using MQTTnet;
using MQTTnet.Client;


namespace Domovoy.Connectivity.Services;

public class AdapterManager : BackgroundService
{
    private readonly IEnumerable<IProtocolAdapter> _adapters;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<AdapterManager> _logger;
    private IMqttClient? _mqttClient;

    public AdapterManager(
        IEnumerable<IProtocolAdapter> adapters,
        IMessageBus messageBus,
        ILogger<AdapterManager> logger)
    {
        _adapters = adapters;
        _messageBus = messageBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AdapterManager starting with {Count} adapters...", _adapters.Count());

        // 1. Connect MQTT
        await ConnectToMqtt(stoppingToken);

        // 2. Start Adapters
        foreach (var adapter in _adapters)
        {
            try
            {
                adapter.OnDeviceDiscovered += OnDeviceDiscovered;
                adapter.OnDeviceStateChanged += OnDeviceStateChanged;
                await adapter.StartAsync(_mqttClient!, stoppingToken);
                _logger.LogInformation("Started adapter: {Adapter}", adapter.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start adapter: {Adapter}", adapter.Name);
            }
        }

        // 3. Subscribe to Device Commands (from Bus)
        await SubscribeToCommands(stoppingToken);

        _logger.LogInformation("AdapterManager ready.");

        // Keep alive
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task ConnectToMqtt(CancellationToken token)
    {
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        // Configuration should come from appsettings/env, hardcoded 'rabbitmq'/localhost logic is temporary
        var brokerHost = Environment.GetEnvironmentVariable("MQTT__BROKER") ?? "localhost";
        var brokerPort = int.TryParse(Environment.GetEnvironmentVariable("MQTT__PORT"), out var p) ? p : 1883;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId("Domovoy.Connectivity")
            .WithCleanSession()
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessage;

        await _mqttClient.ConnectAsync(options, token);
        _logger.LogInformation("Connected to MQTT Broker at {Host}:{Port}", brokerHost, brokerPort);
    }

    private async Task HandleMqttMessage(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = System.Text.Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

        // Simple routing: Ask each adapter if it can handle the topic
        // Optimization: Use a Dictionary<string, IProtocolAdapter> for known prefixes if performance needed.
        foreach (var adapter in _adapters)
        {
            if (adapter.CanHandleTopic(topic))
            {
                await adapter.HandleMessageAsync(topic, payload);
            }
        }
    }

    private async Task OnDeviceDiscovered(DeviceDiscoveredEvent ev)
    {
        await _messageBus.PublishAsync(
            MessageBusConfiguration.DeviceDiscoveryExchange,
            MessageBusConfiguration.DeviceDiscoveredRoutingKey,
            ev);
        _logger.LogInformation("Published processed discovery: {DeviceId} ({Source})", ev.DeviceId, ev.Source);
    }

    private async Task OnDeviceStateChanged(DeviceStateUpdatedEvent ev)
    {
        // Assuming we have an exchange for State updates.
        // await _messageBus.PublishAsync("domovoy.state", "device.state.changed", ev);
        // For now, logging until exact routing key is confirmed.
        _logger.LogDebug("Device state changed (not published): {DeviceId}", ev.DeviceId);
    }

    private async Task SubscribeToCommands(CancellationToken token)
    {
        // Subscribe to Device Commands
        // Using "connectivity-service-commands" as queue name to be persistent and unique for this service
        await _messageBus.SubscribeAsync<DeviceCommand>(
            "connectivity-service-commands",
            MessageBusConfiguration.DeviceCommandsExchange,
            "#", // Listen to all commands
            HandleDeviceCommand, token);

        _logger.LogInformation("Subscribed to device commands");
    }

    private async Task HandleDeviceCommand(DeviceCommand command)
    {
        foreach (var adapter in _adapters)
        {
            // Ideally we know which adapter owns the device.
            // For now, broadcast.
            await adapter.HandleCommandAsync(command);
        }
    }
}
