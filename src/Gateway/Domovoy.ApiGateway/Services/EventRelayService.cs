// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventRelayService starting - subscribing to state change events");

        // Subscribe to device state changes — CapabilityDeviceManager re-emits normalized state
        // as this event so SignalR clients receive live updates with capability values.
        // Awaited, with the stopping token: a fire-and-forget subscribe swallows a broker failure
        // (the service would report "ready" while relaying nothing) and never unsubscribes on shutdown.
        // The pattern to copy is NotificationRelayService.
        await _messageBus.SubscribeAsync<DeviceStateUpdatedEvent>(
            "apigateway-device-states",
            MessageBusConfiguration.DeviceEventsExchange,
            MessageBusConfiguration.DeviceStateUpdatedRoutingKey,
            HandleDeviceStateChanged,
            stoppingToken);

        _logger.LogInformation("EventRelayService subscriptions complete");
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventRelayService stopping");
        await base.StopAsync(cancellationToken);
    }
}
