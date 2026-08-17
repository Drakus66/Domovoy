// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Time + sun trigger scheduler (roadmap Epic 1A). Ticks once per minute and fires any active
/// rule whose cron schedule matches the current minute, or whose sunrise/sunset (± offset) falls in it.
/// Per-minute resolution keeps it dependency-free and is ample for irrigation/outdoor-lighting.
///
/// <para><b>Cron is wall-clock time at the site</b> (<see cref="SiteContext.TimeZone"/>, persisted with the site
/// location in Epic 2K), not the container's clock. "Water the greenhouse at 07:00" has to mean 07:00 where the
/// house is; matching against the process-local time made the meaning depend on the container's <c>TZ</c> (UTC in
/// the shipped compose), so a household in another timezone got its schedules shifted. It also lets the
/// scene-schedule discovery (Epic 2F × 3B) propose a cron from the local time it observed and have it fire at
/// exactly that time. Until a site timezone is configured, <see cref="SiteContext"/> is UTC — the same behaviour
/// as before.</para>
/// </summary>
public sealed class AutomationScheduler : BackgroundService
{
    private readonly RuleStore _store;
    private readonly SunCalculator _sun;
    private readonly SiteContext _site;
    private readonly RuleRunner _runner;
    private readonly ILogger<AutomationScheduler> _logger;

    public AutomationScheduler(
        RuleStore store, SunCalculator sun, SiteContext site, RuleRunner runner, ILogger<AutomationScheduler> logger)
    {
        _store = store;
        _sun = sun;
        _site = site;
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutomationScheduler started (per-minute cron + sun triggers)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await DelayToNextMinute(stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                Tick(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _site.TimeZone), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler tick failed");
            }
        }
    }

    // `now` is site-local (its offset is the site's); cron reads its wall-clock fields, sun triggers its UTC instant.
    private void Tick(DateTimeOffset now, CancellationToken ct)
    {
        var nowUtc = now.ToUniversalTime();

        foreach (var rule in _store.Rules.Where(r => r.Status is RuleStatus.Active or RuleStatus.Shadow or RuleStatus.BoundedActive))
        {
            foreach (var trigger in rule.Triggers)
            {
                if (trigger.Type == TriggerType.Time && CronMatches(trigger.Cron, now))
                {
                    _runner.Fire(rule, $"schedule {trigger.Cron}", ct);
                    break;
                }

                if (trigger.Type == TriggerType.Sun && trigger.Sun is { } sunEvent && SunMatches(sunEvent, trigger.OffsetMinutes, nowUtc))
                {
                    _runner.Fire(rule, $"{sunEvent}{Offset(trigger.OffsetMinutes)}", ct);
                    break;
                }
            }
        }
    }

    private static bool CronMatches(string? cron, DateTimeOffset now) =>
        CronSchedule.TryParse(cron, out var schedule) && schedule!.Matches(now);

    private bool SunMatches(SunEvent sunEvent, int offsetMinutes, DateTimeOffset nowUtc)
    {
        var (sunrise, sunset) = _sun.ForDate(nowUtc.UtcDateTime);
        var baseTime = sunEvent == SunEvent.Sunrise ? sunrise : sunset;
        if (baseTime is null) return false;

        var target = baseTime.Value.AddMinutes(offsetMinutes);
        return SameMinute(target, nowUtc.UtcDateTime);
    }

    private static bool SameMinute(DateTime a, DateTime b) =>
        a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.Hour == b.Hour && a.Minute == b.Minute;

    private static string Offset(int minutes) =>
        minutes == 0 ? string.Empty : minutes > 0 ? $"+{minutes}m" : $"{minutes}m";

    private static async Task DelayToNextMinute(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset).AddMinutes(1);
        var wait = next - now;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }
}
