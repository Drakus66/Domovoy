// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services.Notifications;
using Domovoy.Contracts.Notifications;
using Domovoy.Contracts.Proposals;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Sends the weekly "living home" digest (roadmap Epic 3J tail 5, extends 2N): once a week, on the configured
/// local day + hour, it gathers the week's intervention dynamics (the trust currency), new findings and proposed
/// amendments, has <see cref="WeeklyDigestBuilder"/> compose the diary-tone lines, and dispatches it through the
/// notification pipeline (Epic 3F) as a <c>proactive</c> nudge. "Silence is a feature" (2N): a quiet week sends
/// nothing. Idempotent per ISO week so a restart / an hourly re-check never double-sends.
/// </summary>
public sealed class WeeklyDigestService : BackgroundService
{
    private const int MaxEvents = 40000;

    private readonly DbGatewayClient _db;
    private readonly NotificationDispatcher _notifications;
    private readonly SiteContext _site;
    private readonly AutomationOptions _options;
    private readonly ILogger<WeeklyDigestService> _logger;

    private string? _lastSentWeekKey;

    public WeeklyDigestService(
        DbGatewayClient db, NotificationDispatcher notifications, SiteContext site,
        IOptions<AutomationOptions> options, ILogger<WeeklyDigestService> logger)
    {
        _db = db;
        _notifications = notifications;
        _site = site;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A short startup delay, then an hourly check — the digest is a once-a-week event, so a coarse tick is fine.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Weekly digest tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!_options.WeeklyDigestEnabled) return;

        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _site.TimeZone);
        if ((int)nowLocal.DayOfWeek != _options.WeeklyDigestDayOfWeek) return;
        if (nowLocal.Hour < Math.Clamp(_options.WeeklyDigestHour, 0, 23)) return;

        var weekKey = $"{ISOWeek.GetYear(nowLocal.DateTime)}-W{ISOWeek.GetWeekOfYear(nowLocal.DateTime):D2}";
        if (string.Equals(weekKey, _lastSentWeekKey, StringComparison.Ordinal)) return; // already handled this week

        var metrics = await GatherAsync(ct);
        // Mark the week handled BEFORE dispatch: whether we send or stay silent, we shouldn't recompute all day.
        _lastSentWeekKey = weekKey;

        var body = WeeklyDigestBuilder.Build(metrics);
        if (body is null)
        {
            _logger.LogDebug("Weekly digest {Week}: quiet week — nothing to send", weekKey);
            return;
        }

        await _notifications.DispatchAsync(
            new NotificationMessage("Итоги недели", body, NotificationSeverities.Info, NotificationCategories.Proactive,
                DedupKey: $"weekly-digest-{weekKey}"),
            ct);
        _logger.LogInformation("Weekly digest {Week} sent", weekKey);
    }

    private async Task<WeeklyDigestBuilder.Metrics> GatherAsync(CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var from = to.AddDays(-7);

        var events = await _db.GetStateEventsAsync(from, to, MaxEvents, ct) ?? new List<DbGatewayClient.EventLogEntry>();
        var firings = InterventionMiner.Firings(events, _options);
        var overrides = firings.Count(f => f.Overridden);

        var proposals = await _db.GetProposalsAsync(ct) ?? new List<Proposal>();
        var recent = proposals.Where(p => p.CreatedAt >= from).ToList();
        var amendments = recent.Count(p => p.Kind == ProposalKind.RuleAmendment);
        var findings = recent.Count(p => p.Kind != ProposalKind.RuleAmendment
            && (string.Equals(p.Source, "discovery", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Source, "ml_proposer", StringComparison.OrdinalIgnoreCase)));

        return new WeeklyDigestBuilder.Metrics(firings.Count, overrides, findings, amendments);
    }
}
