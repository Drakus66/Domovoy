using Domovoy.ApiGateway.Hubs;
using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Events;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.SignalR;

namespace Domovoy.ApiGateway.Services;

/// <summary>
/// Background service that relays Message Bus events to SignalR clients in real-time
/// </summary>
public class EventRelayService : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IHubContext<DeviceHub> _hubContext;
    private readonly ILogger<EventRelayService> _logger;

    public EventRelayService(
        IMessageBus messageBus,
        IHubContext<DeviceHub> hubContext,
        ILogger<EventRelayService> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventRelayService starting - subscribing to state change events");

        // Subscribe to device state changes
        _messageBus.SubscribeAsync<DeviceStateUpdatedEvent>(
            "apigateway-device-states",
            MessageBusConfiguration.DeviceEventsExchange,
            MessageBusConfiguration.DeviceStateUpdatedRoutingKey,
            HandleDeviceStateChanged);

        // Subscribe to device discovery events
        _messageBus.SubscribeAsync<DeviceDiscoveredEvent>(
            "apigateway-device-discovery",
            MessageBusConfiguration.DeviceDiscoveryExchange,
            MessageBusConfiguration.DeviceDiscoveredRoutingKey,
            HandleDeviceDiscovered);

        _logger.LogInformation("EventRelayService subscriptions complete");
        return Task.CompletedTask;
    }

    private async Task HandleDeviceStateChanged(DeviceStateUpdatedEvent stateEvent)
    {
        try
        {
            _logger.LogDebug("Relaying device state change for {DeviceId} to SignalR clients", stateEvent.DeviceId);

            // Push to all connected SignalR clients
            await _hubContext.Clients.All.SendAsync(
                "DeviceStateUpdated",
                stateEvent.DeviceId,
                stateEvent.State);

            _logger.LogDebug("State update relayed to clients for device {DeviceId}", stateEvent.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error relaying device state change for {DeviceId}", stateEvent.DeviceId);
        }
    }

    private async Task HandleDeviceDiscovered(DeviceDiscoveredEvent discoveryEvent)
    {
        try
        {
            _logger.LogDebug("Relaying device discovery for {DeviceId} to SignalR clients", discoveryEvent.DeviceId);

            // Push to all connected SignalR clients
            await _hubContext.Clients.All.SendAsync(
                "DeviceDiscovered",
                discoveryEvent.DeviceId,
                discoveryEvent.DeviceType.ToString(),
                discoveryEvent.Name);

            _logger.LogInformation("Device discovery relayed to clients: {DeviceId}", discoveryEvent.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error relaying device discovery for {DeviceId}", discoveryEvent.DeviceId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventRelayService stopping");
        await base.StopAsync(cancellationToken);
    }
}
