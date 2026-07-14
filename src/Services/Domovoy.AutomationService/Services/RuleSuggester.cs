// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Proposals;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Heuristic rule proposer (roadmap Epic 2C) — the deliberate stub-precursor to the pattern-discovery engine
/// (2F). It mines <b>one</b> pattern from the P0-5 event-log: a sensor becoming active (motion/presence) being
/// followed shortly by a <i>human</i> switch action ("motion in the hall → someone turns the light on"). When a
/// pair recurs often enough (support) and reliably enough (confidence), it queues a <see cref="ProposalKind.Rule"/>
/// candidate — a Proposed <see cref="AutomationRule"/> plus a <see cref="Proposal"/> pointing at it — for human
/// approval. The candidate is validated by 1F replay in the UI before activation; the proposer only supplies
/// hypotheses, it never activates anything (principle 1).
///
/// <para>The invariant that keeps this honest: only actions a human took (<c>triggerSource=user</c>) count as
/// labels — never rule/ML-driven ones — so the system learns from free human choices, not from its own output
/// (no ML-on-ML, no self-fulfilling rules). The full MI/Granger/FDR/rule-mining funnel is Epic 2F; this is one
/// co-occurrence heuristic.</para>
/// </summary>
public sealed class RuleSuggester : BackgroundService
{
    private readonly DbGatewayClient _db;
    private readonly AutomationOptions _options;
    private readonly ILogger<RuleSuggester> _logger;

    // The event-log endpoint caps a single response; the proposer works off recent history, so one page is enough.
    private const int MaxEvents = 20000;

    public RuleSuggester(DbGatewayClient db, IOptions<AutomationOptions> options, ILogger<RuleSuggester> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ProposalScanHours <= 0) return; // periodic scan disabled; manual endpoint still works

        // A small startup delay lets the gateway + event-log come up first.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SuggestOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Rule proposer scan failed"); }

            try { await Task.Delay(TimeSpan.FromHours(_options.ProposalScanHours), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Scan the recent event-log once, queue any new candidates, and report how many were created.</summary>
    public async Task<SuggestResult> SuggestOnceAsync(CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var from = to.AddDays(-Math.Max(1, _options.ProposalWindowDays));

        var events = await _db.GetStateEventsAsync(from, to, MaxEvents, ct);
        if (events is null) return new SuggestResult(0, 0, "event-log unavailable");

        var candidates = Mine(events, _options);
        if (candidates.Count == 0) return new SuggestResult(0, 0, "no candidates");

        // De-dup against what already exists: any rule already wiring this pair, or an open proposal for it.
        var rules = await _db.GetUserRulesAsync(ct) ?? new List<AutomationRule>();
        var openProposals = await _db.GetProposalsAsync(ct, nameof(ProposalStatus.Proposed)) ?? new List<Proposal>();
        var devices = await _db.GetDevicesAsync(ct) ?? new List<DbGatewayClient.DeviceSnapshot>();
        var nameById = devices.ToDictionary(d => d.Id, d => string.IsNullOrEmpty(d.Name) ? d.Id : d.Name);

        var created = 0;
        foreach (var c in candidates)
        {
            if (RuleAlreadyWires(rules, c)) continue;

            var title = TitleFor(c, nameById);
            if (openProposals.Any(p => string.Equals(p.Title, title, StringComparison.Ordinal))) continue;

            var rule = new AutomationRule
            {
                Name = title,
                Description = "Proposed by the pattern heuristic (Epic 2C). Validate with Simulate before approving.",
                Status = RuleStatus.Proposed,
                Triggers =
                {
                    new RuleTrigger
                    {
                        Type = TriggerType.DeviceState,
                        DeviceId = c.TriggerDeviceId,
                        CapabilityId = c.TriggerCapability,
                        Operator = "eq",
                        Value = true,
                    },
                },
                Actions =
                {
                    new RuleAction
                    {
                        Type = ActionType.Command,
                        DeviceId = c.ActionDeviceId,
                        Set = new Dictionary<string, object?> { [CapabilityIds.OnOff] = true },
                    },
                },
            };

            var savedRule = await _db.CreateRuleAsync(rule, ct);
            if (savedRule is null) continue;

            var proposal = new Proposal
            {
                Kind = ProposalKind.Rule,
                Title = title,
                Rationale = $"Seen {c.Support}× in {_options.ProposalWindowDays}d, "
                    + $"confidence {c.Confidence:P0} (P(action|trigger)). Validate with Simulate before approving.",
                Source = "ml_proposer",
                RuleId = savedRule.Id,
                Evidence = new Dictionary<string, double>
                {
                    ["support"] = c.Support,
                    ["confidence"] = c.Confidence,
                    ["windowDays"] = _options.ProposalWindowDays,
                },
            };
            var savedProposal = await _db.CreateProposalAsync(proposal, ct);
            if (savedProposal is null)
            {
                _logger.LogWarning("Created candidate rule {Rule} but failed to queue its proposal", savedRule.Id);
                continue;
            }

            // Track locally so two candidates that resolve to the same title in one scan don't double-create.
            openProposals.Add(savedProposal);
            created++;
        }

        _logger.LogInformation("Rule proposer: {Created} new candidate(s) from {Events} events", created, events.Count);
        return new SuggestResult(candidates.Count, created, created == 0 ? "all candidates already known" : "ok");
    }

    /// <summary>
    /// Mine trigger→action co-occurrences from a chronological event stream (pure, for unit testing). A trigger
    /// is a configured sensor capability becoming truthy; an action is a human (<c>user</c>) <c>on_off</c>→on on
    /// another device within the co-occurrence window. Confidence = fraction of a trigger's firings followed by
    /// the action; support = the count of such firings.
    /// </summary>
    public static List<Candidate> Mine(IReadOnlyList<DbGatewayClient.EventLogEntry> events, AutomationOptions options)
    {
        var triggerCaps = new HashSet<string>(options.ProposalTriggerCapabilities, StringComparer.OrdinalIgnoreCase);
        var window = TimeSpan.FromSeconds(Math.Max(1, options.ProposalCoWindowSeconds));

        // Events arrive oldest-first (the gateway client reverses to chronological). Split into trigger firings
        // and human on/off actions, both already time-ordered.
        var triggers = events.Where(e => triggerCaps.Contains(e.CapabilityId) && IsTruthy(e.NewValue)).ToList();
        var actions = events
            .Where(e => string.Equals(e.CapabilityId, CapabilityIds.OnOff, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.TriggerSource, "user", StringComparison.OrdinalIgnoreCase)
                && IsTruthy(e.NewValue))
            .ToList();
        if (triggers.Count == 0 || actions.Count == 0) return new List<Candidate>();

        var triggerCount = new Dictionary<(string Dev, string Cap), int>();
        var coCount = new Dictionary<(string Dev, string Cap, string Action), int>();

        // Two-pointer: trigger window starts advance monotonically, so the action cursor never rewinds.
        var lo = 0;
        foreach (var t in triggers)
        {
            var akey = (t.DeviceId, t.CapabilityId);
            triggerCount[akey] = triggerCount.GetValueOrDefault(akey) + 1;

            while (lo < actions.Count && actions[lo].Timestamp <= t.Timestamp) lo++;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var j = lo; j < actions.Count && actions[j].Timestamp <= t.Timestamp + window; j++)
            {
                var bdev = actions[j].DeviceId;
                if (string.Equals(bdev, t.DeviceId, StringComparison.Ordinal)) continue; // don't wire a device to itself
                if (seen.Add(bdev))
                {
                    var ckey = (t.DeviceId, t.CapabilityId, bdev);
                    coCount[ckey] = coCount.GetValueOrDefault(ckey) + 1;
                }
            }
        }

        var result = new List<Candidate>();
        foreach (var ((dev, cap, action), support) in coCount)
        {
            if (support < options.ProposalMinSupport) continue;
            var fired = triggerCount.GetValueOrDefault((dev, cap));
            var confidence = fired == 0 ? 0 : (double)support / fired;
            if (confidence < options.ProposalMinConfidence) continue;
            result.Add(new Candidate(dev, cap, action, support, Math.Round(confidence, 3)));
        }

        // Strongest patterns first; cap so one noisy window can't flood the queue.
        return result.OrderByDescending(c => c.Support).ThenByDescending(c => c.Confidence).Take(10).ToList();
    }

    private static bool RuleAlreadyWires(IEnumerable<AutomationRule> rules, Candidate c) =>
        rules.Any(r =>
            r.Triggers.Any(t => t.Type == TriggerType.DeviceState
                && string.Equals(t.DeviceId, c.TriggerDeviceId, StringComparison.Ordinal)
                && string.Equals(t.CapabilityId, c.TriggerCapability, StringComparison.OrdinalIgnoreCase))
            && r.Actions.Any(a => a.Type == ActionType.Command
                && string.Equals(a.DeviceId, c.ActionDeviceId, StringComparison.Ordinal)));

    private static string TitleFor(Candidate c, IReadOnlyDictionary<string, string> nameById)
    {
        var trigger = nameById.GetValueOrDefault(c.TriggerDeviceId, c.TriggerDeviceId);
        var action = nameById.GetValueOrDefault(c.ActionDeviceId, c.ActionDeviceId);
        return $"Turn on \"{action}\" when {c.TriggerCapability} detected by \"{trigger}\"";
    }

    // Truthy across the shapes the event-log stores a "became active" transition in (bool / number / string).
    private static bool IsTruthy(JsonElement? value) => value is { } e && e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => e.TryGetDouble(out var d) && d != 0,
        JsonValueKind.String => bool.TryParse(e.GetString(), out var b) && b,
        _ => false,
    };

    /// <summary>One mined trigger→action pattern above the support/confidence thresholds.</summary>
    public sealed record Candidate(
        string TriggerDeviceId, string TriggerCapability, string ActionDeviceId, int Support, double Confidence);

    /// <summary>Outcome of a scan: how many patterns qualified, how many were newly queued, and a note.</summary>
    public sealed record SuggestResult(int Candidates, int Created, string Note);
}
