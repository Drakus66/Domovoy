using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>Replay/simulation request: a candidate rule + how many days of history to run it over.</summary>
public sealed record ReplayRequest(AutomationRule Rule, int Days);

/// <summary>One point in history where the rule's trigger matched.</summary>
public sealed record ReplayHit(DateTime Timestamp, string TriggerSummary, bool ConditionsMet);

/// <summary>
/// Outcome of replaying a rule over history: how many would have actually fired (trigger matched AND
/// conditions held), the matches, and any caveats (e.g. unsupported trigger types, capped window).
/// </summary>
public sealed record ReplayResult(int EventsScanned, int Fires, List<ReplayHit> Hits, List<string> Notes);

/// <summary>
/// Dry-run a candidate rule against the historical event-log (roadmap Epic 1F — replay/simulation).
/// Walks the P0-5 device-state deltas in chronological order, reconstructing device state as it goes,
/// and evaluates the rule's device-state triggers + conditions at each step <b>without executing any
/// command</b>. The answer to "when would this rule have fired last week?" — the trust bridge that lets
/// a new (or ML-proposed) rule be validated against reality before activation. Reuses the exact same
/// <see cref="RuleEvaluator"/> the live engine uses, so simulation matches production semantics.
/// </summary>
public sealed class ReplayService
{
    private readonly DbGatewayClient _db;
    private readonly SunCalculator _sun;
    private readonly ILogger<ReplayService> _logger;

    private const int MaxEvents = 5000;

    public ReplayService(DbGatewayClient db, SunCalculator sun, ILogger<ReplayService> logger)
    {
        _db = db;
        _sun = sun;
        _logger = logger;
    }

    public async Task<ReplayResult> RunAsync(ReplayRequest request, CancellationToken ct)
    {
        var rule = request.Rule;
        var notes = new List<string>();

        var deviceTriggers = rule.Triggers.Where(t => t.Type == TriggerType.DeviceState).ToList();
        if (rule.Triggers.Any(t => t.Type != TriggerType.DeviceState))
            notes.Add("Only device-state triggers are simulated; time/sun triggers are not replayed.");
        if (deviceTriggers.Count == 0)
        {
            notes.Add("Rule has no device-state trigger to simulate over history.");
            return new ReplayResult(0, 0, new(), notes);
        }

        var days = Math.Clamp(request.Days <= 0 ? 7 : request.Days, 1, 90);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-days);

        var events = await _db.GetStateEventsAsync(from, to, MaxEvents, ct);
        if (events is null)
        {
            notes.Add("Event-log is unavailable — cannot simulate.");
            return new ReplayResult(0, 0, new(), notes);
        }
        if (events.Count >= MaxEvents)
            notes.Add($"History capped at {MaxEvents} events; narrow the window for full coverage.");

        // Fresh registry + evaluator bound to the reconstructed state (not the live engine's).
        var registry = new DeviceRegistry();
        var evaluator = new RuleEvaluator(registry, _sun);
        var hits = new List<ReplayHit>();

        foreach (var e in events)
        {
            if (!Guid.TryParse(e.DeviceId, out var deviceId)) continue;

            registry.SetZone(deviceId, e.ZoneId);
            var newValue = e.NewValue.HasValue ? (object?)e.NewValue.Value : null;
            var oldValue = registry.SetValue(deviceId, e.CapabilityId, newValue);
            var current = registry.GetValue(deviceId, e.CapabilityId);

            if (!deviceTriggers.Any(t => evaluator.DeviceTriggerMatches(t, deviceId, e.CapabilityId, current, oldValue)))
                continue;

            // Evaluate conditions at the historical moment (local time + the mode stamped on the event).
            var at = new DateTimeOffset(DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc)).ToLocalTime();
            var conditionsMet = evaluator.ConditionsHold(rule.Conditions, at, e.Mode);

            hits.Add(new ReplayHit(e.Timestamp, $"{e.CapabilityId}={Display(current)} on {e.DeviceId}", conditionsMet));
        }

        var fires = hits.Count(h => h.ConditionsMet);
        _logger.LogInformation("Replay of '{Rule}' over {Days}d: {Scanned} events, {Fires} would fire",
            rule.Name, days, events.Count, fires);

        return new ReplayResult(events.Count, fires, hits, notes);
    }

    private static string Display(object? v) => v switch
    {
        null => "null",
        bool b => b ? "on" : "off",
        _ => v.ToString() ?? string.Empty
    };
}
