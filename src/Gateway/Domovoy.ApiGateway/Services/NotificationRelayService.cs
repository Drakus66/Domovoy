// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.ApiGateway.Hubs;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.SignalR;

namespace Domovoy.ApiGateway.Services;

/// <summary>
/// Relays the first-party LAN notification channel (2M.2) to connected clients. The AutomationService's
/// SignalRChannel publishes <see cref="NotificationRaisedV1"/> on the bus (it can't reach the hub, which lives
/// here); this service subscribes and pushes each one to every connected client as a <c>NotificationRaised</c>
/// hub event, which the WebUI turns into an in-app banner. Mirrors <see cref="EventRelayService"/>.
/// </summary>
public sealed class NotificationRelayService : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly IHubContext<DeviceHub> _hub;
    private readonly ILogger<NotificationRelayService> _logger;

    public NotificationRelayService(IMessageBus bus, IHubContext<DeviceHub> hub, ILogger<NotificationRelayService> logger)
    {
        _bus = bus;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.SubscribeAsync<Envelope<NotificationRaisedV1>>(
            "apigateway-notifications",
            BusTopology.EventsExchange,
            BusTopology.NotificationRaisedKey,
            HandleAsync);

        _logger.LogInformation("NotificationRelayService ready — relaying LAN notifications to SignalR clients");
    }

    private async Task HandleAsync(Envelope<NotificationRaisedV1> envelope)
    {
        var n = envelope.Data;
        if (n is null) return;

        try
        {
            // A plain object so the JS client reads it as { title, body, severity, category, raisedAt }.
            await _hub.Clients.All.SendAsync("NotificationRaised", new
            {
                title = n.Title,
                body = n.Body,
                severity = n.Severity,
                category = n.Category,
                raisedAt = n.RaisedAt,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relay notification to SignalR clients");
        }
    }
}
