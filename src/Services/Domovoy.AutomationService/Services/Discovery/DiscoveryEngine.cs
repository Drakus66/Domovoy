using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Proposals;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// The pattern-discovery engine (roadmap Epic 2F) — the autonomous layer that <b>itself</b> finds regularities
/// in accumulated history and formulates them as candidates, rather than only executing the ones a human named
/// (2A/2B) or the single co-occurrence the 2C heuristic mines. It runs the full <see cref="PatternMiner"/> funnel
/// (MI screening + FDR + condition mining) over the P0-5 event-log, then queues each survivor as a
/// <c>Proposed</c> <see cref="AutomationRule"/> plus a <see cref="Proposal"/> (<c>Source=discovery</c>) for human
/// approval and staged rollout — reusing the exact same approval path and 1F replay validation as every other
/// proposal.
///
/// <para>Placement mirrors <see cref="MlTrainingService"/> (2A) and <see cref="RuleSuggester"/> (2C): a periodic
/// <see cref="BackgroundService"/> plus a manual <c>POST /api/discovery/scan</c>, reading history over HTTP from
/// the DbGateway (never touching Mongo directly). <b>Invariant (principle 1):</b> the engine is only a supplier
/// of hypotheses — nothing it produces activates without a person approving it.</para>
/// </summary>
public sealed class DiscoveryEngine : BackgroundService
{
    private readonly DbGatewayClient _db;
    private readonly AutomationOptions _options;
    private readonly ILogger<DiscoveryEngine> _logger;

    // The event-log endpoint caps a single response; the engine works off a recent window, so one page suffices.
    private const int MaxEvents = 40000;

    public DiscoveryEngine(DbGatewayClient db, IOptions<AutomationOptions> options, ILogger<DiscoveryEngine> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.DiscoveryScanHours <= 0) return; // periodic scan disabled; manual endpoint still works

        // A longer startup delay than the trainer's — discovery is best-effort background work, never on a hot path.
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Pattern-discovery scan failed"); }

            try { await Task.Delay(TimeSpan.FromHours(_options.DiscoveryScanHours), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Mine the recent event-log once, queue any new candidates, and report what happened.</summary>
    public async Task<ScanResult> ScanOnceAsync(CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var from = to.AddDays(-Math.Max(1, _options.DiscoveryWindowDays));

        var events = await _db.GetStateEventsAsync(from, to, MaxEvents, ct);
        if (events is null) return new ScanResult(0, 0, "event-log unavailable");

        var patterns = PatternMiner.Mine(events, _options);
        if (patterns.Count == 0) return new ScanResult(0, 0, "no patterns found");

        var rules = await _db.GetUserRulesAsync(ct) ?? new List<AutomationRule>();
        var openProposals = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Proposed)) ?? new List<Proposal>();
        var devices = await _db.GetDevicesAsync(ct) ?? new List<DbGatewayClient.DeviceSnapshot>();
        var nameById = devices.ToDictionary(d => d.Id, d => string.IsNullOrEmpty(d.Name) ? d.Id : d.Name);

        var created = 0;
        foreach (var p in patterns)
        {
            if (AlreadyWired(rules, p)) continue;

            var title = TitleFor(p, nameById);
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var rule = BuildRule(p, title);
            var savedRule = await _db.CreateRuleAsync(rule, ct);
            if (savedRule is null) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.Rule,
                Title = title,
                Rationale = RationaleFor(p),
                Source = "discovery",
                RuleId = savedRule.Id,
            };
            var savedProposal = await _db.CreateProposalAsync(proposal, ct);
            if (savedProposal is null)
            {
                _logger.LogWarning("Created candidate rule {Rule} but failed to queue its proposal", savedRule.Id);
                continue;
            }

            openProposals.Add(savedProposal);
            if (++created >= _options.DiscoveryMaxProposals) break;
        }

        _logger.LogInformation("Pattern discovery: {Created} new candidate(s) from {Patterns} patterns / {Events} events",
            created, patterns.Count, events.Count);
        return new ScanResult(patterns.Count, created, created == 0 ? "all patterns already known" : "ok");
    }

    private static AutomationRule BuildRule(PatternMiner.DiscoveredPattern p, string title)
    {
        var rule = new AutomationRule
        {
            Name = title,
            Description = "Found by the pattern-discovery engine (Epic 2F). Validate with Simulate before approving.",
            Status = RuleStatus.Proposed,
            Triggers =
            {
                new RuleTrigger
                {
                    Type = TriggerType.DeviceState,
                    DeviceId = p.TriggerDeviceId,
                    CapabilityId = p.TriggerCapability,
                    Operator = p.TriggerOperator,
                    Value = p.TriggerValue,
                },
            },
            Actions =
            {
                new RuleAction
                {
                    Type = ActionType.Command,
                    DeviceId = p.ActionDeviceId,
                    Set = new Dictionary<string, object?> { [CapabilityIds.OnOff] = true },
                },
            },
        };

        // Attach the time-of-day guard the miner found sharpened the pattern (1A condition, replayable by 1F).
        if (p.FromTime is not null && p.ToTime is not null)
            rule.Conditions.Add(new RuleCondition { Type = ConditionType.TimeOfDay, FromTime = p.FromTime, ToTime = p.ToTime });

        return rule;
    }

    // Don't re-propose a pair a user rule already wires (same trigger device+capability → same action device).
    private static bool AlreadyWired(IEnumerable<AutomationRule> rules, PatternMiner.DiscoveredPattern p) =>
        rules.Any(r =>
            r.Triggers.Any(t => t.Type == TriggerType.DeviceState
                && string.Equals(t.DeviceId, p.TriggerDeviceId, StringComparison.Ordinal)
                && string.Equals(t.CapabilityId, p.TriggerCapability, StringComparison.OrdinalIgnoreCase))
            && r.Actions.Any(a => a.Type == ActionType.Command
                && string.Equals(a.DeviceId, p.ActionDeviceId, StringComparison.Ordinal)));

    private static string TitleFor(PatternMiner.DiscoveredPattern p, IReadOnlyDictionary<string, string> nameById)
    {
        var trigger = nameById.GetValueOrDefault(p.TriggerDeviceId, p.TriggerDeviceId);
        var action = nameById.GetValueOrDefault(p.ActionDeviceId, p.ActionDeviceId);
        var when = p.TriggerOperator == "eq"
            ? $"{p.TriggerCapability} detected by \"{trigger}\""
            : $"\"{trigger}\" {p.TriggerCapability} {(p.TriggerOperator == "lt" ? "<" : ">")} {p.TriggerValue}";
        var window = p.FromTime is not null ? $" ({p.FromTime}–{p.ToTime})" : string.Empty;
        return $"Turn on \"{action}\" when {when}{window}";
    }

    private static string RationaleFor(PatternMiner.DiscoveredPattern p) =>
        $"Discovered: when {p.ConditionText}"
        + (p.FromTime is not null ? $" during {p.FromTime}–{p.ToTime}" : string.Empty)
        + $", a person turned this on {p.Support}× (confidence {p.Confidence:P0}, "
        + $"{p.Lift:0.0}× the base rate {p.BaseRate:P0}; MI {p.MutualInfo:0.###} nats, p {p.PValue:0.###}). "
        + "Validate with Simulate before approving.";

    /// <summary>Outcome of a scan: patterns that qualified, how many were newly queued, and a note.</summary>
    public sealed record ScanResult(int Patterns, int Created, string Note);
}
