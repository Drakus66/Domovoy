using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Events;
using Domovoy.MessageBus;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Background service that intercepts all state change events from the message bus
/// and persists them to MongoDB automatically
/// </summary>
public class EventInterceptor : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IMongoDatabase _database;
    private readonly ILogger<EventInterceptor> _logger;

    public EventInterceptor(
        IMessageBus messageBus,
        IMongoDatabase database,
        ILogger<EventInterceptor> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventInterceptor starting - subscribing to state change events");

        // Subscribe to device state changes
        _messageBus.SubscribeAsync<DeviceStateUpdatedEvent>(
            "dbgateway-device-states",
            MessageBusConfiguration.DeviceEventsExchange,
            MessageBusConfiguration.DeviceStateUpdatedRoutingKey,
            HandleDeviceStateChanged);

        // Subscribe to device discovery events
        _messageBus.SubscribeAsync<DeviceDiscoveredEvent>(
            "dbgateway-device-discovery",
            MessageBusConfiguration.DeviceDiscoveryExchange,
            MessageBusConfiguration.DeviceDiscoveredRoutingKey,
            HandleDeviceDiscovered);

        // Subscribe to light state changes (when implemented)
        _messageBus.SubscribeAsync<DeviceStateUpdatedEvent>(
            "dbgateway-light-states",
            MessageBusConfiguration.DeviceEventsExchange,
            "event.light.state.changed",
            HandleLightStateChanged);

        _logger.LogInformation("EventInterceptor subscriptions complete");
        return Task.CompletedTask;
    }

    private async Task HandleDeviceStateChanged(DeviceStateUpdatedEvent stateEvent)
    {
        try
        {
            _logger.LogDebug("Intercepted device state change for {DeviceId}", stateEvent.DeviceId);

            var collection = _database.GetCollection<DeviceStateUpdatedEvent>("device_states");

            // Create filter to update existing state or insert new
            var filter = Builders<DeviceStateUpdatedEvent>.Filter.Eq(e => e.DeviceId, stateEvent.DeviceId);

            var options = new ReplaceOptions { IsUpsert = true };

            await collection.ReplaceOneAsync(filter, stateEvent, options);

            _logger.LogInformation("Persisted state change for device {DeviceId}", stateEvent.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting device state change for {DeviceId}", stateEvent.DeviceId);
        }
    }

    private async Task HandleDeviceDiscovered(DeviceDiscoveredEvent discoveryEvent)
    {
        try
        {
            _logger.LogDebug("Intercepted device discovery for {DeviceId}", discoveryEvent.DeviceId);

            var collection = _database.GetCollection<DeviceDiscoveredEvent>("device_discoveries");

            await collection.InsertOneAsync(discoveryEvent);

            _logger.LogInformation("Persisted device discovery for {DeviceId} of type {DeviceType}",
                discoveryEvent.DeviceId, discoveryEvent.DeviceType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting device discovery for {DeviceId}", discoveryEvent.DeviceId);
        }
    }

    private async Task HandleLightStateChanged(DeviceStateUpdatedEvent stateEvent)
    {
        try
        {
            _logger.LogDebug("Intercepted light state change for {DeviceId}", stateEvent.DeviceId);

            var collection = _database.GetCollection<DeviceStateUpdatedEvent>("light_states");

            var filter = Builders<DeviceStateUpdatedEvent>.Filter.Eq(e => e.DeviceId, stateEvent.DeviceId);
            var options = new ReplaceOptions { IsUpsert = true };

            await collection.ReplaceOneAsync(filter, stateEvent, options);

            _logger.LogInformation("Persisted light state change for device {DeviceId}", stateEvent.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting light state change for {DeviceId}", stateEvent.DeviceId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventInterceptor stopping");
        await base.StopAsync(cancellationToken);
    }
}
