// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Seasonal threshold drift (roadmap Epic 3J tail 4): a numeric-trigger rule whose threshold no longer matches
/// how the household actually behaves — e.g. "lights on when illuminance &lt; 50" but in autumn people keep
/// undoing it because it's already dark by then. The signal: at the moments the household <b>overrode</b> the
/// rule, what did the trigger's own sensor read? If those readings cluster away from the current threshold by
/// more than a floor, propose shifting the threshold to that lived-in value.
///
/// <para>Pure and conservative: it fires only on a genuine numeric device-state trigger, needs enough overrides
/// each with a resolvable sensor reading, and proposes only when the drift exceeds
/// <see cref="AutomationOptions.DriftMinShift"/>. Approval applies it as a <c>set_threshold</c> amendment; nothing
/// changes without a person (principle 1).</para>
/// </summary>
public static class ThresholdDriftMiner
{
    /// <summary>A rule whose numeric trigger threshold has drifted from the household's lived-in value.</summary>
    public sealed record DriftCandidate(
        string RuleId, string CapabilityId, double CurrentThreshold, double SuggestedThreshold, int Samples);

    public static List<DriftCandidate> Mine(
        IReadOnlyList<DbGatewayClient.EventLogEntry> events,
        IReadOnlyList<AutomationRule> rules,
        IReadOnlyList<InterventionMiner.Firing> firings,
        AutomationOptions options)
    {
        var overridesByRule = firings
            .Where(f => f.Overridden && f.OverriddenAt is not null)
            .GroupBy(f => f.RuleId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.OverriddenAt!.Value).OrderBy(t => t).ToList(), StringComparer.Ordinal);

        var result = new List<DriftCandidate>();

        foreach (var rule in rules)
        {
            if (rule.Status is not (RuleStatus.Active or RuleStatus.BoundedActive)) continue;

            var trigger = rule.Triggers.FirstOrDefault(tr =>
                tr.Type == TriggerType.DeviceState && !string.IsNullOrEmpty(tr.CapabilityId)
                && IsThresholdOp(tr.Operator) && TryDouble(tr.Value, out _));
            if (trigger is null || !TryDouble(trigger.Value, out var threshold)) continue;

            if (!overridesByRule.TryGetValue(rule.Id, out var overrideTimes) || overrideTimes.Count < options.DriftMinOverrides)
                continue;

            // The readings of the trigger's OWN sensor over the window, chronological. Scoping matters: a
            // capability-only filter mixes every device reporting it (three illuminance sensors in three rooms
            // would average into one meaningless "lived-in value"). A device-scoped trigger reads that device, a
            // zone-scoped one reads that zone, and only a house-wide trigger (neither set) reads them all — the
            // same breadth the trigger itself watches.
            var series = events
                .Where(e => string.Equals(e.CapabilityId, trigger.CapabilityId, StringComparison.OrdinalIgnoreCase)
                    && MatchesTriggerScope(e, trigger)
                    && TryDoubleJson(e.NewValue, out _))
                .Select(e => (e.Timestamp, Value: JsonDouble(e.NewValue)))
                .OrderBy(x => x.Timestamp)
                .ToList();
            if (series.Count == 0) continue;

            var samples = new List<double>();
            foreach (var t in overrideTimes)
                if (LastValueAtOrBefore(series, t) is double d) samples.Add(d);
            if (samples.Count < options.DriftMinOverrides) continue;

            var suggested = Math.Round(Median(samples), 2);
            if (Math.Abs(suggested - threshold) < options.DriftMinShift) continue;

            result.Add(new DriftCandidate(rule.Id, trigger.CapabilityId!, threshold, suggested, samples.Count));
        }

        return result.OrderByDescending(c => Math.Abs(c.SuggestedThreshold - c.CurrentThreshold)).ToList();
    }

    private static bool IsThresholdOp(string? op) =>
        op is "lt" or "gt" or "lte" or "gte";

    // Whether an event comes from the source the trigger actually watches: its device, else its zone, else
    // anything (a trigger naming neither is house-wide by design).
    private static bool MatchesTriggerScope(DbGatewayClient.EventLogEntry e, RuleTrigger trigger)
    {
        if (!string.IsNullOrEmpty(trigger.DeviceId))
            return string.Equals(e.DeviceId, trigger.DeviceId, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(trigger.ZoneId))
            return string.Equals(e.ZoneId, trigger.ZoneId, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    // The most recent reading at or before an override moment (the sensor value the household disagreed with).
    private static double? LastValueAtOrBefore(List<(DateTime Timestamp, double Value)> series, DateTime at)
    {
        double? last = null;
        foreach (var (ts, v) in series)
        {
            if (ts > at) break;
            last = v;
        }
        return last;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>Coerce a rule's <c>object?</c> threshold (BSON double/int, JSON element, boxed number) to double.</summary>
    private static bool TryDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case decimal m: result = (double)m; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetDouble(out var jd): result = jd; return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sd):
                result = sd; return true;
            default: result = 0; return false;
        }
    }

    private static bool TryDoubleJson(JsonElement? value, out double result)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Number } je && je.TryGetDouble(out result)) return true;
        result = 0;
        return false;
    }

    private static double JsonDouble(JsonElement? value) =>
        value is JsonElement je && je.ValueKind == JsonValueKind.Number ? je.GetDouble() : 0;
}
