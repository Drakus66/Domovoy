// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.ApiGateway.Hubs;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.SignalR;

namespace Domovoy.ApiGateway.Services;

/// <summary>
/// Relays live device state to SignalR clients.
///
/// <para>Subscribes to the <b>capability contract itself</b> (<see cref="DeviceStateReportV1"/>), which is
/// what adapters publish. Until the UnifiedDeviceService was retired this went through an extra hop: that
/// service consumed the capability report and re-emitted it verbatim as a legacy <c>DeviceStateUpdatedEvent</c>
/// on a second exchange, purely because this relay once spoke only the legacy shape. The translation was the
/// last thing it did — a container and a bus hop on every state change, for a rename.</para>
///
/// <para>The client-facing event is unchanged: <c>DeviceStateUpdated(deviceId, state)</c>, with null values
/// dropped exactly as the retired shim dropped them (a null means "this capability reported nothing", and the
/// dashboard treats an absent key and a null key differently).</para>
///
/// <para>Mirrors <see cref="NotificationRelayService"/>.</para>
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
        _logger.LogInformation("EventRelayService starting - subscribing to capability state reports");

        // Awaited, with the stopping token: a fire-and-forget subscribe swallows a broker failure
        // (the service would report "ready" while relaying nothing) and never unsubscribes on shutdown.
        await _messageBus.SubscribeAsync<Envelope<DeviceStateReportV1>>(
            "apigateway-device-states",
            BusTopology.StateExchange,
            BusTopology.DeviceStateUpdatedKey,
            HandleStateReport,
            stoppingToken);

        _logger.LogInformation("EventRelayService subscriptions complete");
    }

    private async Task HandleStateReport(Envelope<DeviceStateReportV1> envelope)
    {
        var report = envelope.Data;
        if (report is null) return;

        try
        {
            var state = report.State
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!);

            await _hubContext.Clients.All.SendAsync("DeviceStateUpdated", report.DeviceId.ToString(), state);

            _logger.LogDebug(
                "State update relayed to clients for device {DeviceId} ({Count} capabilities)",
                report.DeviceId, state.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error relaying device state change for {DeviceId}", report.DeviceId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventRelayService stopping");
        await base.StopAsync(cancellationToken);
    }
}
