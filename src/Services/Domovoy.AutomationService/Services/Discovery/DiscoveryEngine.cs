// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Ml;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Ml;
using Domovoy.Contracts.Proposals;
using Domovoy.Contracts.Scenes;

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
    private const string Source = MlActivitySources.Discovery;

    private readonly DbGatewayClient _db;
    private readonly MlProposerGate _gate;
    private readonly AutomationOptions _options;
    private readonly ILogger<DiscoveryEngine> _logger;

    // The event-log endpoint caps a single response; the engine works off a recent window, so one page suffices.
    private const int MaxEvents = 40000;

    public DiscoveryEngine(DbGatewayClient db, MlProposerGate gate, IOptions<AutomationOptions> options, ILogger<DiscoveryEngine> logger)
    {
        _db = db;
        _gate = gate;
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
            try
            {
                // Epic 3I: gate the periodic scan (layer + proposers on, history mature). Manual "search now"
                // bypasses the gate via ScanOnceAsync — an explicit human action is never spam.
                var gate = await _gate.EvaluateAsync(stoppingToken);
                if (gate.Allowed) await ScanOnceAsync(stoppingToken);
                else await _gate.WriteSkippedAsync(Source, gate, stoppingToken);
            }
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
        if (events is null)
        {
            await _gate.WriteErrorAsync(Source, "event-log unavailable", ct);
            return new ScanResult(0, 0, "event-log unavailable");
        }

        var patterns = PatternMiner.Mine(events, _options);
        var preferences = SetpointPreferenceMiner.Mine(events, _options);              // Epic 2F type B (scheduled)
        var modelCandidates = SetpointPreferenceMiner.MineModelCandidates(events, _options); // Epic 2F type B (ML)

        var rules = await _db.GetUserRulesAsync(ct) ?? new List<AutomationRule>();
        var openProposals = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Proposed)) ?? new List<Proposal>();
        // Epic 3I rejection memory: fold already-rejected proposals into the dedup set so a pattern the user
        // declined (by title, or by ML-task target) is not re-proposed on the next scan. Every dedup below reads
        // openProposals, so one merge covers rule/scene titles AND ml-task targets.
        var rejected = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Rejected)) ?? new List<Proposal>();
        openProposals.AddRange(rejected);
        var devices = await _db.GetDevicesAsync(ct) ?? new List<DbGatewayClient.DeviceSnapshot>();
        var scenes = await _db.GetScenesAsync(ct) ?? new List<Scene>();

        // Epic 2F × 3B: a repeatedly hand-arranged zone state → a scene proposal; an existing scene the user keeps
        // activating at a consistent time → a schedule-rule proposal.
        var sceneCandidates = SceneConfigurationMiner.Mine(events, devices, scenes, _options);
        var sceneSchedules = SceneActivationMiner.Mine(events, scenes, _options);

        // Epic 3J "living rules": rules the household systematically overrides → a "retire this rule?" proposal.
        var deadRules = InterventionMiner.Mine(events, _options);
        // Epic 3J tails: the per-firing signal drives self-correcting (amend, don't retire) and threshold drift.
        var firings = InterventionMiner.Firings(events, _options);
        var refinements = OverrideMiner.Mine(firings, _options);
        var drifts = ThresholdDriftMiner.Mine(events, rules, firings, _options);

        if (patterns.Count == 0 && preferences.Count == 0 && modelCandidates.Count == 0
            && sceneCandidates.Count == 0 && sceneSchedules.Count == 0 && deadRules.Count == 0
            && refinements.Count == 0 && drifts.Count == 0)
        {
            await _gate.WriteScanAsync(Source, 0, 0, "no patterns found", HypothesisMetrics(events.Count, 0), ct);
            return new ScanResult(0, 0, "no patterns found");
        }

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

        // Scene-configuration proposals (Epic 2F × 3B): a repeatedly hand-arranged zone state → a new scene,
        // optionally bundled with a daily schedule rule when the arrangement also clusters at one time of day.
        foreach (var cand in sceneCandidates)
        {
            if (created >= _options.DiscoveryMaxProposals) break;

            var title = SceneTitle(cand, nameById);
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.Scene,
                Title = title,
                Rationale = SceneRationale(cand, nameById),
                Source = "discovery",
                SceneDraft = new Scene
                {
                    Name = SuggestedSceneName(cand, nameById),
                    Targets = cand.Targets
                        .Select(t => new SceneTarget { DeviceId = t.DeviceId, Set = new Dictionary<string, object?>(t.Set) })
                        .ToList(),
                },
                SceneScheduleCron = cand.ScheduleMinute is int m ? DailyCron(m) : null,
                Evidence = new Dictionary<string, double>
                {
                    ["support"] = cand.Support,
                    ["devices"] = cand.Targets.Count,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
            };
            if (cand.ScheduleMinute is int min)
            {
                proposal.Evidence["scheduleMinute"] = min;
                proposal.Evidence["scheduleSpread"] = cand.ScheduleSpread;
            }

            var saved = await _db.CreateProposalAsync(proposal, ct);
            if (saved is null) continue;
            openProposals.Add(saved);
            created++;
        }

        // Scene-schedule proposals (Epic 2F × 3B): an existing scene the user keeps activating at ~the same time
        // → a Proposed daily rule that activates it (ActionType.Scene). Reuses the Rule approval path + 1F replay.
        foreach (var sched in sceneSchedules)
        {
            if (created >= _options.DiscoveryMaxProposals) break;
            if (SceneScheduleAlreadyWired(rules, sched)) continue;

            var title = SceneScheduleTitle(sched);
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var rule = BuildSceneScheduleRule(sched, title);
            var savedRule = await _db.CreateRuleAsync(rule, ct);
            if (savedRule is null) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.Rule,
                Title = title,
                Rationale = SceneScheduleRationale(sched),
                Source = "discovery",
                RuleId = savedRule.Id,
                Evidence = new Dictionary<string, double>
                {
                    ["support"] = sched.Support,
                    ["scheduleMinute"] = sched.Minute,
                    ["scheduleSpread"] = sched.Spread,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
            };
            var savedProposal = await _db.CreateProposalAsync(proposal, ct);
            if (savedProposal is null) continue;
            openProposals.Add(savedProposal);
            created++;
        }

        // Epic 3J "living rules": propose retiring a rule the user overrode in most of its firings. Only ever a
        // live rule (Active/Bounded), deduped against open + already-rejected amendment proposals for it.
        foreach (var dr in deadRules)
        {
            if (created >= _options.DiscoveryMaxProposals) break;

            var rule = rules.FirstOrDefault(r => string.Equals(r.Id, dr.RuleId, StringComparison.Ordinal));
            if (rule is null) continue;
            if (rule.Status is not (RuleStatus.Active or RuleStatus.BoundedActive)) continue;
            if (openProposals.Any(p => p.Kind == ProposalKind.RuleAmendment
                && string.Equals(p.RuleId, dr.RuleId, StringComparison.Ordinal))) continue;

            var title = $"Retire rule \"{rule.Name}\"?";
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.RuleAmendment,
                Title = title,
                Rationale = $"You overrode \"{rule.Name}\" in {dr.Overrides} of its {dr.Firings} runs "
                    + $"({dr.OverrideRate:P0}) — it may no longer match how you use the home. Approving disables it "
                    + "(re-enable any time on the Automations page).",
                Source = "intervention",
                RuleId = dr.RuleId,
                AmendmentAction = "disable",
                Evidence = new Dictionary<string, double>
                {
                    ["firings"] = dr.Firings,
                    ["overrides"] = dr.Overrides,
                    ["overrideRate"] = dr.OverrideRate,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
            };
            var saved = await _db.CreateProposalAsync(proposal, ct);
            if (saved is null) continue;
            openProposals.Add(saved);
            created++;
        }

        // Epic 3J tail 2 "self-correcting rules": a rule fought only in one time band → add an exception, not retire.
        foreach (var rf in refinements)
        {
            if (created >= _options.DiscoveryMaxProposals) break;

            var rule = rules.FirstOrDefault(r => string.Equals(r.Id, rf.RuleId, StringComparison.Ordinal));
            if (rule is null || rule.Status is not (RuleStatus.Active or RuleStatus.BoundedActive)) continue;
            if (openProposals.Any(p => p.Kind == ProposalKind.RuleAmendment && p.AmendmentAction == "add_condition"
                && string.Equals(p.RuleId, rf.RuleId, StringComparison.Ordinal))) continue;

            var title = $"Refine rule \"{rule.Name}\"?";
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.RuleAmendment,
                Title = title,
                Rationale = $"You overrode \"{rule.Name}\" in {rf.Overrides} of its {rf.Firings} runs between "
                    + $"{rf.FromHour:D2}:00 and {rf.ToHour % 24:D2}:00 ({rf.OverrideRate:P0}) — but it looks fine the "
                    + "rest of the day. Approving adds a time-of-day exception so it only runs outside those hours "
                    + "(edit any time on the Automations page).",
                Source = "intervention",
                RuleId = rf.RuleId,
                AmendmentAction = "add_condition",
                AmendmentCondition = OverrideMiner.ExceptionCondition(rf),
                Evidence = new Dictionary<string, double>
                {
                    ["firings"] = rf.Firings,
                    ["overrides"] = rf.Overrides,
                    ["overrideRate"] = rf.OverrideRate,
                    ["fromHour"] = rf.FromHour,
                    ["toHour"] = rf.ToHour % 24,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
            };
            var saved = await _db.CreateProposalAsync(proposal, ct);
            if (saved is null) continue;
            openProposals.Add(saved);
            created++;
        }

        // Epic 3J tail 4 "seasonal threshold drift": a numeric trigger the household keeps beating → shift its value.
        foreach (var dr in drifts)
        {
            if (created >= _options.DiscoveryMaxProposals) break;

            var rule = rules.FirstOrDefault(r => string.Equals(r.Id, dr.RuleId, StringComparison.Ordinal));
            if (rule is null || rule.Status is not (RuleStatus.Active or RuleStatus.BoundedActive)) continue;
            if (openProposals.Any(p => p.Kind == ProposalKind.RuleAmendment && p.AmendmentAction == "set_threshold"
                && string.Equals(p.RuleId, dr.RuleId, StringComparison.Ordinal))) continue;

            var title = $"Adjust \"{rule.Name}\" threshold?";
            if (openProposals.Any(x => string.Equals(x.Title, title, StringComparison.Ordinal))) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.RuleAmendment,
                Title = title,
                Rationale = $"When you overrode \"{rule.Name}\", {dr.CapabilityId} was usually around "
                    + $"{dr.SuggestedThreshold:0.#}, not its trigger value {dr.CurrentThreshold:0.#} — the threshold may have "
                    + $"drifted (season/habit). Approving sets it to {dr.SuggestedThreshold:0.#} (edit any time).",
                Source = "intervention",
                RuleId = dr.RuleId,
                AmendmentAction = "set_threshold",
                AmendmentCapabilityId = dr.CapabilityId,
                AmendmentValue = dr.SuggestedThreshold,
                Evidence = new Dictionary<string, double>
                {
                    ["currentThreshold"] = dr.CurrentThreshold,
                    ["suggestedThreshold"] = dr.SuggestedThreshold,
                    ["samples"] = dr.Samples,
                    ["windowDays"] = _options.DiscoveryWindowDays,
                },
            };
            var saved = await _db.CreateProposalAsync(proposal, ct);
            if (saved is null) continue;
            openProposals.Add(saved);
            created++;
        }

        _logger.LogInformation(
            "Pattern discovery: {Created} new candidate(s) from {Patterns} patterns + {Prefs} setpoint prefs + {Models} ml-setpoints "
            + "+ {Scenes} scene configs + {SceneSched} scene schedules + {Dead} dead-rules + {Refine} refinements + {Drift} drifts / {Events} events",
            created, patterns.Count, preferences.Count, modelCandidates.Count, sceneCandidates.Count, sceneSchedules.Count, deadRules.Count, refinements.Count, drifts.Count, events.Count);
        var total = patterns.Count + preferences.Count + modelCandidates.Count + sceneCandidates.Count + sceneSchedules.Count
            + deadRules.Count + refinements.Count + drifts.Count;

        // Epic 3I: journal the pulse (how many hypotheses survived the funnel, how many were queued) and raise one
        // notification when something new landed in the queue.
        var note = created > 0 ? "queued discovery proposal(s)" : "all patterns already known";
        await _gate.WriteScanAsync(Source, total, created, note, HypothesisMetrics(events.Count, total), ct);
        await _gate.NotifyFindingsAsync(created, ct);

        return new ScanResult(total, created, created == 0 ? "all patterns already known" : "ok");
    }

    // The counters the ML journal renders for a discovery cycle: the window's event volume and how many
    // hypotheses survived the MI/FDR funnel this run.
    private Dictionary<string, double> HypothesisMetrics(int events, int hypotheses) => new()
    {
        ["events"] = events,
        ["hypotheses"] = hypotheses,
        ["windowDays"] = _options.DiscoveryWindowDays,
    };

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

    // ----- Scene-configuration proposals (Epic 2F × 3B) -----

    private static string DailyCron(int minuteOfDay) => $"{minuteOfDay % 60} {minuteOfDay / 60} * * *";

    private static string TimeText(int minuteOfDay) => $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    private static string SceneDevicesLabel(
        SceneConfigurationMiner.SceneCandidate cand, IReadOnlyDictionary<string, string> nameById) =>
        string.Join(" + ", cand.Targets.Take(3).Select(t => nameById.GetValueOrDefault(t.DeviceId, t.DeviceId)))
        + (cand.Targets.Count > 3 ? " …" : "");

    // English title = the stable dedup key (all device ids), so the same configuration is not re-proposed.
    private static string SceneTitle(
        SceneConfigurationMiner.SceneCandidate cand, IReadOnlyDictionary<string, string> nameById)
    {
        var names = string.Join(" + ", cand.Targets.Select(t => nameById.GetValueOrDefault(t.DeviceId, t.DeviceId)));
        var when = cand.ScheduleMinute is int m ? $" at {TimeText(m)}" : string.Empty;
        return $"New scene: {names}{when}";
    }

    private static string SuggestedSceneName(
        SceneConfigurationMiner.SceneCandidate cand, IReadOnlyDictionary<string, string> nameById) =>
        SceneDevicesLabel(cand, nameById);

    private static string SceneRationale(
        SceneConfigurationMiner.SceneCandidate cand, IReadOnlyDictionary<string, string> nameById) =>
        $"Discovered: this arrangement of {cand.Targets.Count} devices ({SceneDevicesLabel(cand, nameById)}) was set "
        + $"up by hand {cand.Support}× "
        + (cand.ScheduleMinute is int m ? $"around {TimeText(m)} " : string.Empty)
        + "over the mined window. Approving saves it as a scene"
        + (cand.ScheduleMinute is not null ? " and schedules it daily." : ".");

    // ----- Scene-schedule proposals (Epic 2F × 3B) -----

    private static AutomationRule BuildSceneScheduleRule(SceneActivationMiner.SceneSchedule s, string title) => new()
    {
        Name = title,
        Description = "Found by scene-schedule discovery (Epic 2F × 3B). Validate with Simulate before approving.",
        Status = RuleStatus.Proposed,
        Triggers = { new RuleTrigger { Type = TriggerType.Time, Cron = DailyCron(s.Minute) } },
        Actions = { new RuleAction { Type = ActionType.Scene, SceneId = s.SceneId } },
    };

    private static bool SceneScheduleAlreadyWired(IEnumerable<AutomationRule> rules, SceneActivationMiner.SceneSchedule s) =>
        rules.Any(r => r.Triggers.Any(t => t.Type == TriggerType.Time)
            && r.Actions.Any(a => a.Type == ActionType.Scene && string.Equals(a.SceneId, s.SceneId, StringComparison.Ordinal)));

    private static string SceneScheduleTitle(SceneActivationMiner.SceneSchedule s) =>
        $"Activate scene \"{s.SceneName}\" daily at {TimeText(s.Minute)}";

    private static string SceneScheduleRationale(SceneActivationMiner.SceneSchedule s) =>
        $"Discovered: you activated \"{s.SceneName}\" {s.Support}× around {TimeText(s.Minute)} (spread ±{s.Spread} min). "
        + "Validate with Simulate before approving.";

    /// <summary>Outcome of a scan: patterns that qualified, how many were newly queued, and a note.</summary>
    public sealed record ScanResult(int Patterns, int Created, string Note);
}
