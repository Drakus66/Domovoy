// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// First-party LAN notification channel (2M.2). The dispatcher lives here in the AutomationService but the
/// SignalR hub (<c>DeviceHub</c>) lives in the ApiGateway, so this channel can't call the hub directly — it
/// publishes <see cref="NotificationRaisedV1"/> on the bus and the ApiGateway's NotificationRelayService fans it
/// out to connected clients as an in-app banner. Fully local, survives no-internet, and on by default (it's the
/// primary notification surface, with no external dependency).
/// </summary>
public sealed class SignalRChannel : INotificationChannel
{
    private readonly IMessageBus _bus;
    private readonly LanChannelOptions _options;
    private readonly ILogger<SignalRChannel> _logger;

    public SignalRChannel(IMessageBus bus, IOptions<NotificationOptions> options, ILogger<SignalRChannel> logger)
    {
        _bus = bus;
        _options = options.Value.Lan;
        _logger = logger;
    }

    public string Name => "lan";

    public bool Enabled => _options.Enabled;

    // The in-app banner is quiet: it only exists while a browser is open (Epic 3F safety floor).
    public NotificationVisibility Visibility => NotificationVisibility.Quiet;

    public async Task<bool> SendAsync(NotificationMessage message, CancellationToken ct)
    {
        try
        {
            var envelope = Envelope<NotificationRaisedV1>.Create(
                MessageTypes.NotificationRaised,
                source: "automation-service",
                data: new NotificationRaisedV1(
                    message.Title,
                    message.Body,
                    message.Severity,
                    DateTimeOffset.UtcNow,
                    message.Category,
                    message.Actions));

            await _bus.PublishAsync(
                BusTopology.EventsExchange,
                BusTopology.NotificationRaisedKey,
                envelope,
                ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LAN (SignalR) notification publish failed");
            return false;
        }
    }
}
