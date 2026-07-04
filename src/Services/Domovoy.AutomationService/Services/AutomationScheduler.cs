using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Time + sun trigger scheduler (roadmap Epic 1A). Ticks once per minute and fires any active/protected
/// rule whose cron schedule matches the current minute, or whose sunrise/sunset (± offset) falls in it.
/// Per-minute resolution keeps it dependency-free and is ample for irrigation/outdoor-lighting.
/// </summary>
public sealed class AutomationScheduler : BackgroundService
{
    private readonly RuleStore _store;
    private readonly SunCalculator _sun;
    private readonly RuleRunner _runner;
    private readonly ILogger<AutomationScheduler> _logger;

    public AutomationScheduler(RuleStore store, SunCalculator sun, RuleRunner runner, ILogger<AutomationScheduler> logger)
    {
        _store = store;
        _sun = sun;
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
                Tick(DateTimeOffset.Now, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler tick failed");
            }
        }
    }

    private void Tick(DateTimeOffset now, CancellationToken ct)
    {
        var nowUtc = now.ToUniversalTime();

        foreach (var rule in _store.Rules.Where(r => r.IsProtected || r.Status is RuleStatus.Active or RuleStatus.Shadow))
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
