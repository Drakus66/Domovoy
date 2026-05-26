using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Connectivity.Adapters;
using Domovoy.MessageBus;
using MQTTnet;
using MQTTnet.Client;
using Microsoft.Extensions.Options;


namespace Domovoy.Connectivity.Services;

public class AdapterManager : BackgroundService
{
    private readonly IEnumerable<IProtocolAdapter> _adapters;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<AdapterManager> _logger;
    private readonly IOptions<RabbitMqConfig> _config;
    private IMqttClient? _mqttClient;
    
    // In-Memory routing map: DeviceId -> Adapter instance
    private readonly Dictionary<Guid, IProtocolAdapter> _deviceToAdapterMap = new();

    public AdapterManager(
        IEnumerable<IProtocolAdapter> adapters,
        IMessageBus messageBus,
        IOptions<RabbitMqConfig> config,
        ILogger<AdapterManager> logger)
    {
        _adapters = adapters;
        _messageBus = messageBus;
        _config = config;
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
                adapter.OnAdapterStateReported += OnAdapterStateReported;
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
        var brokerHost = Environment.GetEnvironmentVariable("MQTT__BROKER") ?? _config.Value.HostName ?? "localhost";
        var brokerPort = int.TryParse(Environment.GetEnvironmentVariable("MQTT__PORT"), out var p) ? p : _config.Value.MqttPort;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId("Domovoy.Connectivity")
            .WithCredentials(_config.Value.UserName, _config.Value.Password)
            .WithCleanSession()
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += HandleMqttMessage;

        await _mqttClient.ConnectAsync(options, token);
        _logger.LogInformation("Connected to MQTT Broker at {Host}:{Port} as user {User}", brokerHost, brokerPort, _config.Value.UserName);
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
        // 1. Find which adapter sent this (by looking at event Source or matching adapters)
        var sourceAdapter = _adapters.FirstOrDefault(a => a.Name == ev.Source);
        
        if (sourceAdapter != null)
        {
            // Cache the routing in memory
            _deviceToAdapterMap[ev.DeviceId] = sourceAdapter;
            _logger.LogInformation("Cached route: Device {DeviceId} -> Adapter {AdapterName}", ev.DeviceId, sourceAdapter.Name);
        }

        // 2. Publish to the bus so UnifiedDeviceManager can save it
        await _messageBus.PublishAsync(
            MessageBusConfiguration.DeviceDiscoveryExchange,
            MessageBusConfiguration.DeviceDiscoveredRoutingKey,
            ev);
            
        _logger.LogInformation("Published processed discovery: {DeviceId} ({Source})", ev.DeviceId, ev.Source);
    }

    private async Task OnAdapterStateReported(AdapterStateReportedEvent ev)
    {
        // Publish to RMQ so the UnifiedDeviceManager can resolve it to a physical device.
        // Exchange/routing key come from shared constants so they always match the subscriber.
        await _messageBus.PublishAsync(
            MessageBusConfiguration.AdapterStateExchange,
            MessageBusConfiguration.AdapterStateReportedRoutingKey,
            ev);

        _logger.LogDebug("Adapter state reported: {Source} -> {Topic}", ev.AdapterSource, ev.Topic);
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
        IProtocolAdapter? targetAdapter = null;

        // 1. Check in-memory cache
        if (_deviceToAdapterMap.TryGetValue(command.DeviceId, out var cachedAdapter))
        {
            targetAdapter = cachedAdapter;
        }
        // 2. Fallback to enriched metadata from UnifiedDeviceManager
        else if (command.Parameters.TryGetValue("AdapterSource", out var sourceObj) && sourceObj is string sourceName)
        {
            targetAdapter = _adapters.FirstOrDefault(a => a.Name == sourceName);
            if (targetAdapter != null)
            {
                // Self-healing: Restore cache from DB-enriched parameters
                _deviceToAdapterMap[command.DeviceId] = targetAdapter;
                _logger.LogInformation("Restored route from metadata: Device {DeviceId} -> Adapter {AdapterName}", 
                    command.DeviceId, targetAdapter.Name);
            }
        }

        // 3. Execute
        if (targetAdapter != null)
        {
            _logger.LogInformation("Routing command {CommandType} for {DeviceId} to adapter {AdapterName}", 
                command.CommandTypes, command.DeviceId, targetAdapter.Name);
            await targetAdapter.HandleCommandAsync(command);
        }
        else
        {
            _logger.LogWarning("No route found for device {DeviceId} (no cache, no AdapterSource in parameters). Dropping command.", command.DeviceId);
        }
    }
}
