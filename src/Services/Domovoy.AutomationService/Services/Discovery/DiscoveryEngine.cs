// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Ml;
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
        var preferences = SetpointPreferenceMiner.Mine(events, _options);              // Epic 2F type B (scheduled)
        var modelCandidates = SetpointPreferenceMiner.MineModelCandidates(events, _options); // Epic 2F type B (ML)
        if (patterns.Count == 0 && preferences.Count == 0 && modelCandidates.Count == 0)
            return new ScanResult(0, 0, "no patterns found");

        var rules = await _db.GetUserRulesAsync(ct) ?? new List<AutomationRule>();
        var openProposals = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Proposed)) ?? new List<Proposal>();
        var devices = await _db.GetDevicesAsync(ct) ?? new List<DbGatewayClient.DeviceSnapshot>();
        var nameById = devices.ToDictionary(d => d.Id, d => string.IsNullOrEmpty(d.Name) ? d.Id : d.Name);
        // Epic 2D: device archetypes annotate the proposal so a reviewer sees the semantics of the pair.
        var archetypeById = devices.ToDictionary(d => d.Id, d => d.EffectiveArchetype);

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
                Rationale = RationaleFor(p) + ArchetypeNote(p, archetypeById),
                Source = "discovery",
                RuleId = savedRule.Id,
                Evidence = new Dictionary<string, double>
                {
                    ["support"] = p.Support,
                    ["confidence"] = p.Confidence,
                    ["lift"] = p.Lift,
                    ["baseRate"] = p.BaseRate,
                    ["mi"] = p.MutualInfo,
                    ["p"] = p.PValue,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
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

        // Type-B (Epic 2F): learned setpoint preferences → time-triggered "set this value" proposals.
        foreach (var pref in preferences)
        {
            if (created >= _options.DiscoveryMaxProposals) break;
            if (SetpointAlreadyWired(rules, pref)) continue;

            var title = SetpointTitle(pref, nameById);
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var rule = BuildSetpointRule(pref, title);
            var savedRule = await _db.CreateRuleAsync(rule, ct);
            if (savedRule is null) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.Rule,
                Title = title,
                Rationale = SetpointRationale(pref),
                Source = "discovery",
                RuleId = savedRule.Id,
                Evidence = new Dictionary<string, double>
                {
                    ["support"] = pref.Support,
                    ["value"] = pref.Value,
                    ["stdDev"] = pref.StdDev,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
            };
            var savedProposal = await _db.CreateProposalAsync(proposal, ct);
            if (savedProposal is null) continue;

            openProposals.Add(savedProposal);
            created++;
        }

        // Type-B ML form (Epic 2F): setpoints the user varies with temporal structure → "learn this" ML-task
        // proposals. Distinct from the scheduled preference above — a model captures what a fixed schedule can't.
        // Deduped by target capability against existing ml_tasks + open ML-task proposals (as MlTaskSuggester does).
        if (modelCandidates.Count > 0 && created < _options.DiscoveryMaxProposals)
        {
            var tasks = await _db.GetMlTasksAsync(ct) ?? new List<MlTask>();
            var takenTargets = new HashSet<string>(
                tasks.Select(t => t.TargetCapability)
                    .Concat(openProposals.Where(p => p.Kind == ProposalKind.MlTask && p.MlTaskTarget is not null)
                        .Select(p => p.MlTaskTarget!)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var cand in modelCandidates)
            {
                if (created >= _options.DiscoveryMaxProposals) break;
                if (!takenTargets.Add(cand.CapabilityId)) continue; // one task per capability

                var savedProposal = await _db.CreateProposalAsync(BuildModelProposal(cand, nameById), ct);
                if (savedProposal is null) continue;

                openProposals.Add(savedProposal);
                created++;
            }
        }

        _logger.LogInformation(
            "Pattern discovery: {Created} new candidate(s) from {Patterns} patterns + {Prefs} setpoint prefs + {Models} ml-setpoints / {Events} events",
            created, patterns.Count, preferences.Count, modelCandidates.Count, events.Count);
        return new ScanResult(patterns.Count + preferences.Count + modelCandidates.Count, created,
            created == 0 ? "all patterns already known" : "ok");
    }

    // A learned-setpoint candidate → an "start learning X" ML-task proposal (Epic 2P approval flow).
    private Proposal BuildModelProposal(
        SetpointPreferenceMiner.SetpointModelCandidate cand, IReadOnlyDictionary<string, string> nameById)
    {
        var device = nameById.GetValueOrDefault(cand.DeviceId, cand.DeviceId);
        return new Proposal
        {
            Kind = ProposalKind.MlTask,
            Title = $"Start learning '{cand.CapabilityId}'",
            Rationale = $"Discovered: a person keeps changing {cand.CapabilityId} on \"{device}\" "
                + $"({cand.Samples} settings, spread ±{cand.OverallStdDev}) and {cand.ExplainedByTime:P0} of that "
                + "tracks the time of day — too varied for one schedule, but a model can learn it. "
                + "Approving creates the training task; tune its window/limits on the ML page.",
            Source = "discovery",
            MlTaskTarget = cand.CapabilityId,
            Evidence = new Dictionary<string, double>
            {
                ["samples"] = cand.Samples,
                ["stdDev"] = cand.OverallStdDev,
                ["explained"] = cand.ExplainedByTime,
                ["windowDays"] = _options.DiscoveryWindowDays,
            },
        };
    }

    // ----- Type-B setpoint-preference proposals (Epic 2F) -----

    // A scheduled preference: at the bucket's start each day, set the numeric setpoint to the learned value.
    private static AutomationRule BuildSetpointRule(SetpointPreferenceMiner.SetpointPreference pref, string title)
    {
        var hour = pref.Bucket * 6;
        return new AutomationRule
        {
            Name = title,
            Description = "Found by setpoint-preference discovery (Epic 2F, type B). Validate with Simulate before approving.",
            Status = RuleStatus.Proposed,
            Triggers = { new RuleTrigger { Type = TriggerType.Time, Cron = $"0 {hour} * * *" } },
            Actions =
            {
                new RuleAction
                {
                    Type = ActionType.Command,
                    DeviceId = pref.DeviceId,
                    Set = new Dictionary<string, object?> { [pref.CapabilityId] = pref.Value },
                },
            },
        };
    }

    private static bool SetpointAlreadyWired(IEnumerable<AutomationRule> rules, SetpointPreferenceMiner.SetpointPreference pref) =>
        rules.Any(r => r.Actions.Any(a => a.Type == ActionType.Command
            && string.Equals(a.DeviceId, pref.DeviceId, StringComparison.Ordinal)
            && a.Set is not null && a.Set.ContainsKey(pref.CapabilityId))
            && r.Triggers.Any(t => t.Type == TriggerType.Time && (t.Cron?.StartsWith($"0 {pref.Bucket * 6} ") ?? false)));

    private static string SetpointTitle(SetpointPreferenceMiner.SetpointPreference pref, IReadOnlyDictionary<string, string> nameById)
    {
        var device = nameById.GetValueOrDefault(pref.DeviceId, pref.DeviceId);
        return $"Set \"{device}\" {pref.CapabilityId} to {pref.Value} at {pref.FromTime}–{pref.ToTime}";
    }

    private static string SetpointRationale(SetpointPreferenceMiner.SetpointPreference pref) =>
        $"Discovered: a person set {pref.CapabilityId} to ~{pref.Value} {pref.Support}× during {pref.FromTime}–{pref.ToTime} "
        + $"(spread ±{pref.StdDev}). Validate with Simulate before approving.";

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

    // Epic 2D: annotate the pair with device archetypes (e.g. "motion → light") so the reviewer sees the
    // semantics at a glance. Silent when either archetype is unknown.
    private static string ArchetypeNote(PatternMiner.DiscoveredPattern p, IReadOnlyDictionary<string, string> archetypeById)
    {
        var trigger = archetypeById.GetValueOrDefault(p.TriggerDeviceId, DeviceArchetypes.Unknown);
        var action = archetypeById.GetValueOrDefault(p.ActionDeviceId, DeviceArchetypes.Unknown);
        var known = !string.Equals(trigger, DeviceArchetypes.Unknown, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(action, DeviceArchetypes.Unknown, StringComparison.OrdinalIgnoreCase);
        return known ? $" [{trigger} → {action}]" : string.Empty;
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
