// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Notifications;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services.Notifications;

/// <summary>
/// Bus ingress for notification sources that live outside this process (roadmap Epic 3F discipline; first
/// consumer is the Epic 3K delivery service).
///
/// <para><b>Why this exists.</b> The discipline — per-category routing, mute, rate-limit/dedup, the safety
/// floor — is implemented by <see cref="NotificationDispatcher"/>, which is in-process. Another service
/// cannot call it, and publishing <see cref="NotificationRaisedV1"/> instead lands on the
/// <see cref="SignalRChannel"/>'s <i>output</i> key: the message would reach the in-app banner and could
/// never reach ntfy/Telegram/webhook, with no mute, no rate-limit and no safety floor applied. So an
/// external source publishes <see cref="NotificationRequestV1"/> here and gets the same treatment as an
/// in-process one.</para>
/// </summary>
public sealed class NotificationIngressService : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<NotificationIngressService> _logger;

    public NotificationIngressService(
        IMessageBus bus, NotificationDispatcher dispatcher, ILogger<NotificationIngressService> logger)
    {
        _bus = bus;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.SubscribeAsync<Envelope<NotificationRequestV1>>(
            "automation-notification-requests",
            BusTopology.EventsExchange,
            BusTopology.NotificationRequestedKey,
            envelope => HandleAsync(envelope, stoppingToken));

        _logger.LogInformation(
            "NotificationIngressService ready — out-of-process notifications go through the 3F discipline");
    }

    private async Task HandleAsync(Envelope<NotificationRequestV1> envelope, CancellationToken ct)
    {
        var request = envelope.Data;
        if (request is null || string.IsNullOrWhiteSpace(request.Title)) return;

        var message = new NotificationMessage(
            request.Title,
            request.Body,
            request.Severity,
            NotificationCategories.Normalize(request.Category),
            request.Actions,
            request.DedupKey);

        var delivered = await _dispatcher.DispatchAsync(message, ct);
        _logger.LogDebug(
            "Notification request from {Source} delivered to {Count} channel(s): {Title}",
            envelope.Source, delivered, request.Title);
    }
}
