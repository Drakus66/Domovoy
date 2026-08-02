// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Notifications;
using Domovoy.MessageBus;
using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// Periodically asks the registry whether the channel has anything newer, and tells the household
/// once when it does (roadmap Epic 3K).
///
/// <para><b>Notification discipline (Epic 3F) applies.</b> An available update is
/// <c>proactive</c>/<c>info</c> — never <c>critical</c>, never rate-limit-exempt: it is an offer, not
/// an alarm. It is raised once per version set, so an unattended house does not accumulate one
/// reminder per polling interval.</para>
///
/// <para><b>Offline-first.</b> A failed check is silent. The registry is not in any hot path, and a
/// home with no internet is a supported state, not a fault to report.</para>
/// </summary>
public sealed class UpdateCheckService : BackgroundService
{
    private readonly UpdaterOptions _options;
    private readonly UpdateCatalog _catalog;
    private readonly IMessageBus _bus;
    private readonly ILogger<UpdateCheckService> _logger;

    private string? _lastAnnounced;

    public UpdateCheckService(
        UpdaterOptions options, UpdateCatalog catalog, IMessageBus bus, ILogger<UpdateCheckService> logger)
    {
        _options = options;
        _catalog = catalog;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CheckEnabled)
        {
            _logger.LogInformation("Проверка обновлений отключена настройкой");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(_options.InitialCheckDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Проверка обновлений не удалась — повторим по расписанию");
            }

            await Task.Delay(TimeSpan.FromHours(Math.Max(1, _options.CheckIntervalHours)), stoppingToken);
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        var catalog = await _catalog.BuildAsync(ct);
        var outdated = catalog.Outdated;

        if (outdated.Count == 0)
        {
            _logger.LogDebug("Обновлений нет (канал {Channel})", catalog.Channel);
            return;
        }

        // Отпечаток набора: пока предлагается то же самое, повторно не тревожим.
        var fingerprint = string.Join(
            ";", outdated.OrderBy(x => x, StringComparer.Ordinal)
                .Select(c => $"{c}={catalog.Newest(c)?.Version}"));

        if (fingerprint == _lastAnnounced) return;
        _lastAnnounced = fingerprint;

        var body = outdated.Count == 1
            ? $"Доступна новая версия компонента «{outdated[0]}»: {catalog.Newest(outdated[0])?.Version}."
            : $"Доступны новые версии ({outdated.Count}): {string.Join(", ", outdated)}.";

        var envelope = Envelope<NotificationRaisedV1>.Create(
            MessageTypes.NotificationRaised,
            source: "updater/check",
            data: new NotificationRaisedV1(
                Title: "Доступно обновление",
                Body: body + " Открыть настройки, чтобы посмотреть, что изменится.",
                Severity: NotificationSeverities.Info,
                RaisedAt: DateTimeOffset.UtcNow,
                Category: NotificationCategories.Proactive,
                Actions: new[]
                {
                    new NotificationAction(
                        Id: "open-updates",
                        Label: "Открыть",
                        Kind: NotificationActionKinds.Open,
                        Params: new Dictionary<string, string> { ["route"] = "/settings#updates" }),
                }),
            subject: "updates");

        await _bus.PublishAsync(BusTopology.EventsExchange, BusTopology.NotificationRaisedKey, envelope);
        _logger.LogInformation("Объявлено обновление: {Fingerprint}", fingerprint);
    }
}
