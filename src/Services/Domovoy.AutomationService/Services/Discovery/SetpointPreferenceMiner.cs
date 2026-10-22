// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Type-B discovery (roadmap Epic 2F): learned numeric <b>setpoint preferences</b>. Where the type-A miner finds
/// discrete "sensor → turn on" rules, this finds the values a person repeatedly dials in — e.g. "you set the
/// bedroom target to 19° every night" — and proposes them as a scheduled preference. Pure and unit-testable over
/// synthetic event streams; the <see cref="DiscoveryEngine"/> turns a preference into a time-triggered command
/// proposal (a person still approves it). Only <b>user</b>-sourced settings count — never the loop's own writes
/// (anti-loop / no ML-on-ML, same invariant as type A).
/// </summary>
public static class SetpointPreferenceMiner
{
    /// <summary>A stable setpoint a user prefers within a time-of-day bucket.</summary>
    public sealed record SetpointPreference(
        string DeviceId,
        string CapabilityId,
        int Bucket,          // 0..3 — a 6h time-of-day bucket (matches the type-A miner)
        double Value,        // the mean preferred value
        int Support,         // how many times the user set it in this bucket
        double StdDev,       // spread of the settings (lower = more stable)
        string FromTime,
        string ToTime);

    /// <summary>
    /// A setpoint the user varies with <b>structure</b> (not a single value) — the ML form of type B. Where
    /// <see cref="SetpointPreference"/> captures "always 19 at night", this captures "you keep changing this and
    /// the change tracks the time of day" — too varied for one scheduled value, but learnable. It maps to a
    /// <see cref="Domovoy.Contracts.Proposals.ProposalKind.MlTask"/> proposal, not a fixed rule.
    /// </summary>
    public sealed record SetpointModelCandidate(
        string DeviceId,
        string CapabilityId,
        int Samples,             // user-set observations
        double OverallStdDev,    // total spread (why one scheduled value doesn't fit)
        double ExplainedByTime); // η² — fraction of variance time-of-day explains (why it's learnable, not noise)

    public static List<SetpointPreference> Mine(
        IReadOnlyList<DbGatewayClient.EventLogEntry> events, AutomationOptions options)
    {
        var caps = new HashSet<string>(options.SetpointPreferenceCapabilities, StringComparer.OrdinalIgnoreCase);
        if (caps.Count == 0) return new();

        // (device, cap, 6h bucket) → the user-set numeric values seen there.
        var groups = new Dictionary<(string Dev, string Cap, int Bucket), List<double>>();

        foreach (var e in events)
        {
            if (!caps.Contains(e.CapabilityId)) continue;
            if (!string.Equals(e.TriggerSource, "user", StringComparison.OrdinalIgnoreCase)) continue; // anti-loop
            if (!TryNumber(e.NewValue, out var v)) continue;

            var bucket = e.Timestamp.Hour / 6; // 0..3
            var key = (e.DeviceId, e.CapabilityId, bucket);
            if (!groups.TryGetValue(key, out var list)) { list = new(); groups[key] = list; }
            list.Add(v);
        }

        var results = new List<SetpointPreference>();
        foreach (var ((dev, cap, bucket), values) in groups)
        {
            if (values.Count < options.SetpointMinSupport) continue;

            var mean = values.Average();
            var std = StdDev(values, mean);
            if (std > options.SetpointMaxStdDev) continue; // too scattered to call a preference

            var (from, to) = BucketWindow(bucket);
            results.Add(new SetpointPreference(
                dev, cap, bucket, Math.Round(mean, 2), values.Count, Math.Round(std, 3), from, to));
        }

        // Strongest (most-repeated, then most-stable) first.
        return results
            .OrderByDescending(r => r.Support)
            .ThenBy(r => r.StdDev)
            .ToList();
    }

    /// <summary>
    /// The ML form of type B (roadmap Epic 2F): find setpoints the user varies with temporal structure — too
    /// scattered for a single scheduled value (<see cref="Mine"/>), yet with enough of the variance explained by
    /// time-of-day (η²) that a model could learn it. These become "start learning X" ML-task proposals rather
    /// than fixed rules. Only user-sourced settings count (anti-loop, same as <see cref="Mine"/>).
    /// </summary>
    public static List<SetpointModelCandidate> MineModelCandidates(
        IReadOnlyList<DbGatewayClient.EventLogEntry> events, AutomationOptions options)
    {
        var caps = new HashSet<string>(options.SetpointPreferenceCapabilities, StringComparer.OrdinalIgnoreCase);
        if (caps.Count == 0) return new();

        // (device, cap) → all user-set values with their 6h time-of-day bucket.
        var series = new Dictionary<(string Dev, string Cap), List<(int Bucket, double Value)>>();
        foreach (var e in events)
        {
            if (!caps.Contains(e.CapabilityId)) continue;
            if (!string.Equals(e.TriggerSource, "user", StringComparison.OrdinalIgnoreCase)) continue; // anti-loop
            if (!TryNumber(e.NewValue, out var v)) continue;

            var key = (e.DeviceId, e.CapabilityId);
            if (!series.TryGetValue(key, out var list)) { list = new(); series[key] = list; }
            list.Add((e.Timestamp.Hour / 6, v));
        }

        var results = new List<SetpointModelCandidate>();
        foreach (var ((dev, cap), points) in series)
        {
            if (points.Count < options.SetpointModelMinSupport) continue;

            var all = points.Select(p => p.Value).ToList();
            var grand = all.Average();
            var totalStd = StdDev(all, grand);
            if (totalStd <= options.SetpointMaxStdDev) continue; // a single value fits → the scheduled form (or none)

            var eta = EtaSquaredByBucket(points, grand);
            if (eta < options.SetpointModelMinExplained) continue; // no temporal structure → noise, don't train

            results.Add(new SetpointModelCandidate(dev, cap, points.Count, Math.Round(totalStd, 3), Math.Round(eta, 3)));
        }

        // Strongest signal first: most history, then most explainable.
        return results
            .OrderByDescending(r => r.Samples)
            .ThenByDescending(r => r.ExplainedByTime)
            .ToList();
    }

    /// <summary>
    /// One-way ANOVA effect size η² = between-group / total variance, grouping by the 6h time-of-day bucket.
    /// 0 = time explains nothing (pure noise); →1 = time explains the setpoint fully. Returns 0 when there is no
    /// spread to explain.
    /// </summary>
    private static double EtaSquaredByBucket(List<(int Bucket, double Value)> points, double grandMean)
    {
        var totalSs = points.Sum(p => (p.Value - grandMean) * (p.Value - grandMean));
        if (totalSs <= 1e-9) return 0;

        var betweenSs = points
            .GroupBy(p => p.Bucket)
            .Sum(g =>
            {
                var mean = g.Average(p => p.Value);
                return g.Count() * (mean - grandMean) * (mean - grandMean);
            });

        return Math.Clamp(betweenSs / totalSs, 0, 1);
    }

    private static double StdDev(List<double> values, double mean)
    {
        if (values.Count < 2) return 0;
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }

    private static (string From, string To) BucketWindow(int bucket)
    {
        var fromH = bucket * 6;
        var toH = fromH + 6;
        return ($"{fromH:00}:00", toH >= 24 ? "23:59" : $"{toH:00}:00");
    }

    private static bool TryNumber(JsonElement? value, out double v)
    {
        v = 0;
        if (value is not { } e) return false;
        return e.ValueKind switch
        {
            JsonValueKind.Number when e.TryGetDouble(out var d) => Set(d, out v),
            JsonValueKind.String when double.TryParse(e.GetString(),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dp) => Set(dp, out v),
            _ => false,
        };

        static bool Set(double d, out double outV) { outV = d; return true; }
    }
}
