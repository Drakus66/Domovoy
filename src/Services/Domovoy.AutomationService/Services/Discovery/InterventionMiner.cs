// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Mines <b>interventions</b> — the times a person undid what an automation just did (roadmap Epic 3J "living
/// rules"). The event-log's attribution (Epic 2N) makes this possible: a rule-driven change carries
/// <c>triggerSource=rule</c> with the rule id in <c>triggerId</c>, a human change carries
/// <c>triggerSource=user</c>. When a user touches the same device+capability within a short window after a rule
/// acted on it, that firing was overridden. Aggregated per rule, a high override-rate over enough firings is a
/// <b>dead-rule</b> signal: the household keeps fighting the rule, so the system offers to retire it (a proposal,
/// never an automatic change — principle 1).
///
/// <para>Pure and deterministic (testable): the intervention signal it produces is the topic 3J's other miners
/// (self-correcting rules, threshold drift) and the trust metric build on.</para>
/// </summary>
public static class InterventionMiner
{
    private const string RuleSource = "rule";
    private const string UserSource = "user";

    /// <summary>One rule the household systematically overrides: how often it fired and how often a person undid it.</summary>
    public sealed record DeadRuleCandidate(string RuleId, int Firings, int Overrides, double OverrideRate);

    /// <summary>One rule firing with whether a person undid it and, if so, when + the value they set (the raw
    /// signal both the self-correcting and threshold-drift miners, Epic 3J, build on).</summary>
    public sealed record Firing(
        string RuleId, string DeviceId, string CapabilityId, DateTime FiredAt, bool Overridden, DateTime? OverriddenAt);

    /// <summary>
    /// Per-firing detail (roadmap Epic 3J): for every rule-attributed change, whether a human touched the same
    /// device+capability within the intervention window. The aggregate miners bucket these by rule (dead rules) or
    /// by context (self-correcting); the drift miner reads the override timestamps. Events are oldest-first.
    /// </summary>
    public static List<Firing> Firings(IReadOnlyList<DbGatewayClient.EventLogEntry> events, AutomationOptions options)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, options.InterventionWindowSeconds));

        var userByTarget = events
            .Where(e => string.Equals(e.TriggerSource, UserSource, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (e.DeviceId, Cap: e.CapabilityId.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Timestamp).OrderBy(t => t).ToList());

        var result = new List<Firing>();
        foreach (var ra in events)
        {
            if (!string.Equals(ra.TriggerSource, RuleSource, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(ra.TriggerId)) continue;

            DateTime? overriddenAt = null;
            if (userByTarget.TryGetValue((ra.DeviceId, ra.CapabilityId.ToLowerInvariant()), out var touches))
                overriddenAt = FirstTouchWithin(touches, ra.Timestamp, window);

            result.Add(new Firing(ra.TriggerId!, ra.DeviceId, ra.CapabilityId, ra.Timestamp, overriddenAt is not null, overriddenAt));
        }
        return result;
    }

    // The first human touch strictly after the firing and within the window, or null. Touches are sorted ascending.
    private static DateTime? FirstTouchWithin(List<DateTime> touches, DateTime firedAt, TimeSpan window)
    {
        foreach (var t in touches)
        {
            if (t <= firedAt) continue;
            return t <= firedAt + window ? t : null;
        }
        return null;
    }

    /// <summary>
    /// Find rules whose firings are overridden by a human at least <see cref="AutomationOptions.DeadRuleMinOverrideRate"/>
    /// of the time over at least <see cref="AutomationOptions.DeadRuleMinFirings"/> firings. Events are oldest-first
    /// (the gateway client reverses to chronological).
    /// </summary>
    public static List<DeadRuleCandidate> Mine(IReadOnlyList<DbGatewayClient.EventLogEntry> events, AutomationOptions options)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, options.InterventionWindowSeconds));

        // Human touches indexed by (device, capability), each list already chronological, so a rule firing can
        // binary-scan for a following override without rescanning the whole stream.
        var userByTarget = events
            .Where(e => string.Equals(e.TriggerSource, UserSource, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (e.DeviceId, Cap: e.CapabilityId.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Timestamp).OrderBy(t => t).ToList());

        var firings = new Dictionary<string, int>(StringComparer.Ordinal);
        var overrides = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var ra in events)
        {
            if (!string.Equals(ra.TriggerSource, RuleSource, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(ra.TriggerId)) continue; // can't attribute a firing to a rule without its id

            firings[ra.TriggerId] = firings.GetValueOrDefault(ra.TriggerId) + 1;

            if (userByTarget.TryGetValue((ra.DeviceId, ra.CapabilityId.ToLowerInvariant()), out var touches)
                && OverriddenWithin(touches, ra.Timestamp, window))
                overrides[ra.TriggerId] = overrides.GetValueOrDefault(ra.TriggerId) + 1;
        }

        var result = new List<DeadRuleCandidate>();
        foreach (var (ruleId, fired) in firings)
        {
            if (fired < options.DeadRuleMinFirings) continue;
            var ov = overrides.GetValueOrDefault(ruleId);
            var rate = (double)ov / fired;
            if (rate < options.DeadRuleMinOverrideRate) continue;
            result.Add(new DeadRuleCandidate(ruleId, fired, ov, Math.Round(rate, 3)));
        }

        // Most-fought rules first; a bounded caller decides how many to queue.
        return result.OrderByDescending(c => c.OverrideRate).ThenByDescending(c => c.Firings).ToList();
    }

    // Is there a human touch strictly after the firing and within the window? Touches are sorted ascending.
    private static bool OverriddenWithin(List<DateTime> touches, DateTime firedAt, TimeSpan window)
    {
        // Linear scan is fine (touches per target are few); find the first touch after firedAt.
        foreach (var t in touches)
        {
            if (t <= firedAt) continue;
            return t <= firedAt + window;
        }
        return false;
    }
}
