// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

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
/// <para><b>Notification discipline (Epic 3F) applies.</b> The announcement is published as a
/// <see cref="NotificationRequestV1"/> — the bus ingress of the AutomationService's dispatcher — so it
/// goes through the same routing, mute, rate-limit and safety rules as every in-process source, and can
/// actually reach ntfy/Telegram/webhook. An available update is <c>proactive</c>/<c>info</c>: it is an
/// offer, not an alarm.</para>
///
/// <para><b>Announced once per version set.</b> The fingerprint of what was offered is kept on disk next
/// to the other run state, so a restart does not re-announce the same versions and an unattended house
/// does not accumulate one reminder per polling interval. The dispatcher's dedup window is a short
/// cross-cutting rate-limit and is not a substitute for that.</para>
///
/// <para><b>Settings are live.</b> The toggle and the interval are re-read from the database on every
/// cycle — they belong to the operator on <c>/settings</c>, not to the container's environment, which is
/// only the fallback for a house that never saved any.</para>
///
/// <para><b>Offline-first.</b> A failed check is quiet: it is stamped on the settings record (so the UI
/// can say when the house last looked) and otherwise ignored. The registry is not in any hot path, and a
/// home with no internet is a supported state, not a fault to report.</para>
/// </summary>
public sealed class UpdateCheckService : BackgroundService
{
    /// <summary>
    /// How often the settings are re-read while checking is switched off. Turning the toggle back on
    /// must not wait for the (possibly multi-hour) check interval; polling the local db-gateway is cheap
    /// and touches the registry not at all.
    /// </summary>
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromMinutes(5);

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
        _lastAnnounced = ReadAnnounced();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(_options.InitialCheckDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = await _catalog.GetPollingSettingsAsync(stoppingToken);
            var wait = TimeSpan.FromHours(settings.CheckIntervalHours);

            if (!settings.CheckEnabled)
            {
                _logger.LogDebug("Проверка обновлений отключена настройкой");
                wait = DisabledPollInterval;
            }
            else
            {
                try
                {
                    await CheckOnceAsync(stoppingToken);
                    await _catalog.RecordCheckResultAsync("ok", stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Проверка обновлений не удалась — повторим по расписанию");
                    await _catalog.RecordCheckResultAsync("error", stoppingToken);
                }
            }

            await Task.Delay(wait, stoppingToken);
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

        var body = outdated.Count == 1
            ? $"Доступна новая версия компонента «{outdated[0]}»: {catalog.Newest(outdated[0])?.Version}."
            : $"Доступны новые версии ({outdated.Count}): {string.Join(", ", outdated)}.";

        var envelope = Envelope<NotificationRequestV1>.Create(
            MessageTypes.NotificationRequested,
            source: "updater/check",
            data: new NotificationRequestV1(
                Title: "Доступно обновление",
                Body: body + " Открыть настройки, чтобы посмотреть, что изменится.",
                Severity: NotificationSeverities.Info,
                RequestedAt: DateTimeOffset.UtcNow,
                Category: NotificationCategories.Proactive,
                Actions: new[]
                {
                    new NotificationAction(
                        Id: "open-updates",
                        Label: "Открыть",
                        Kind: NotificationActionKinds.Open,
                        Params: new Dictionary<string, string> { ["route"] = "/settings#updates" }),
                },
                DedupKey: $"updates:{fingerprint}"),
            subject: "updates");

        await _bus.PublishAsync(BusTopology.EventsExchange, BusTopology.NotificationRequestedKey, envelope, ct);

        _lastAnnounced = fingerprint;
        WriteAnnounced(fingerprint);
        _logger.LogInformation("Объявлено обновление: {Fingerprint}", fingerprint);
    }

    private string? ReadAnnounced()
    {
        try
        {
            return File.Exists(_options.AnnouncedFile)
                ? JsonSerializer.Deserialize<string>(File.ReadAllText(_options.AnnouncedFile))
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось прочитать отметку об объявленном обновлении");
            return null;
        }
    }

    private void WriteAnnounced(string fingerprint)
    {
        try
        {
            Directory.CreateDirectory(_options.StateDirectory);
            File.WriteAllText(_options.AnnouncedFile, JsonSerializer.Serialize(fingerprint));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось сохранить отметку об объявленном обновлении");
        }
    }
}
