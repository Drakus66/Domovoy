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
