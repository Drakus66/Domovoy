// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text;
using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Scenes;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Scene-configuration discovery (roadmap Epic 2F × 3B) — the pure core that notices a household repeatedly
/// arranging a <b>zone</b> into the same multi-device state by hand and proposes bundling that state into a
/// first-class <see cref="Scene"/>. Where the type-A/B miners find one sensor→device rule or one setpoint, this
/// finds a <i>configuration</i>: "you keep turning the living-room lamp + strip on and dimming the ceiling —
/// that's a scene". Pure and unit-testable over synthetic event streams; the <see cref="DiscoveryEngine"/>
/// wraps it with I/O and turns each candidate into a <c>ProposalKind.Scene</c> proposal (a person still
/// approves it — principle 1).
///
/// <para>The funnel:</para>
/// <list type="number">
///   <item><b>Reconstruct</b> each zone's writable state over time from the event-log (seeded from the first
///     event's <c>OldValue</c> / the device read-model, then advanced by every delta).</item>
///   <item><b>Arrangements.</b> A burst of <b>user</b> changes to a zone's devices within
///     <see cref="AutomationOptions.SceneCoWindowSeconds"/> is one deliberate arrangement; at the burst's end
///     the zone's resulting state is snapshotted as one configuration instance (anti-loop: only user touches
///     start/extend a burst — scene/rule/ml/block changes never do, so the engine never mines its own output).</item>
///   <item><b>Cluster.</b> Instances whose <i>on</i>-devices (quantized) match are the same configuration; one
///     recurring ≥ <see cref="AutomationOptions.SceneMinSupport"/> times over ≥
///     <see cref="AutomationOptions.SceneMinDevices"/> devices is a candidate. The all-off state is never a scene.</item>
///   <item><b>Schedule.</b> If the instances also cluster around one time of day (spread ≤
///     <see cref="AutomationOptions.SceneScheduleMaxSpreadMinutes"/>), the candidate carries a suggested daily
///     minute so the engine can bundle a "activate it at this time" rule with the scene.</item>
/// </list>
/// </summary>
public static class SceneConfigurationMiner
{
    /// <summary>One device's proposed state within a discovered scene (mirrors <see cref="SceneTarget"/>).</summary>
    public sealed record SceneTargetDraft(string DeviceId, Dictionary<string, object?> Set);

    /// <summary>A repeatedly hand-arranged zone configuration worth proposing as a scene.</summary>
    public sealed record SceneCandidate(
        string ZoneId,
        IReadOnlyList<SceneTargetDraft> Targets, // the on-devices + their captured writable state
        int Support,                             // how many times this configuration recurred
        int? ScheduleMinute,                     // minute-of-day the arrangement clusters at (else null)
        double ScheduleSpread);                  // population std-dev (minutes) of those times, when scheduled

    public static List<SceneCandidate> Mine(
        IReadOnlyList<DbGatewayClient.EventLogEntry> events,
        IReadOnlyList<DbGatewayClient.DeviceSnapshot> devices,
        IReadOnlyList<Scene> existingScenes,
        AutomationOptions options,
        TimeZoneInfo? siteZone = null)
    {
        if (events.Count == 0 || devices.Count == 0) return new();

        var siteClock = siteZone ?? TimeZoneInfo.Utc;

        var stateCaps = new HashSet<string>(options.SceneStateCapabilities, StringComparer.OrdinalIgnoreCase);
        var minDevices = Math.Max(2, options.SceneMinDevices);
        var minSupport = Math.Max(2, options.SceneMinSupport);
        var coWindow = TimeSpan.FromSeconds(Math.Max(30, options.SceneCoWindowSeconds));

        // Which (device, capability) pairs make up a configuration, and each device's zone.
        var deviceZone = new Dictionary<string, string>(StringComparer.Ordinal);
        var trackedCaps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var d in devices)
        {
            if (string.IsNullOrEmpty(d.ZoneId)) continue; // a scene is a zone concept
            var caps = d.Capabilities
                .Where(c => c.Writable && stateCaps.Contains(c.Id))
                .Select(c => c.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (caps.Count == 0) continue;
            deviceZone[d.Id] = d.ZoneId;
            trackedCaps[d.Id] = caps;
        }
        if (deviceZone.Count == 0) return new();

        var current = SeedState(events, devices, deviceZone, trackedCaps);

        // ----- Walk history once: advance state on every tracked delta, snapshot at each user-arrangement burst -----
        var instances = new List<(string Zone, DateTime End, Dictionary<(string Dev, string Cap), object?> Snapshot)>();

        var hasPending = false;
        string pZone = string.Empty;
        DateTime pEnd = default;
        var pDevices = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<(string, string), object?> pSnapshot = new();

        void Flush()
        {
            if (hasPending && pDevices.Count >= minDevices) instances.Add((pZone, pEnd, pSnapshot));
            hasPending = false;
        }

        foreach (var e in events)
        {
            if (!deviceZone.TryGetValue(e.DeviceId, out var zone)) continue;
            if (!trackedCaps[e.DeviceId].Contains(e.CapabilityId)) continue;

            current[(e.DeviceId, e.CapabilityId)] = ToPrimitive(e.NewValue);

            if (!string.Equals(e.TriggerSource, "user", StringComparison.OrdinalIgnoreCase)) continue; // anti-loop

            if (hasPending && string.Equals(zone, pZone, StringComparison.Ordinal) && (e.Timestamp - pEnd) <= coWindow)
            {
                pDevices.Add(e.DeviceId);
            }
            else
            {
                Flush();
                hasPending = true;
                pZone = zone;
                pDevices = new HashSet<string>(StringComparer.Ordinal) { e.DeviceId };
            }
            pEnd = e.Timestamp;
            pSnapshot = SnapshotZone(current, deviceZone, zone);
        }
        Flush();

        if (instances.Count == 0) return new();

        // ----- Cluster instances by their on-device configuration -----
        var clusters = new Dictionary<string, Cluster>(StringComparer.Ordinal);
        foreach (var (zone, end, snapshot) in instances)
        {
            var onDevices = OnDevices(snapshot);
            if (onDevices.Count < minDevices) continue; // needs ≥ N devices actually on to be a multi-device scene

            var key = ConfigKey(zone, onDevices, options.SceneQuantizeSteps);
            if (!clusters.TryGetValue(key, out var cl)) { cl = new Cluster(zone); clusters[key] = cl; }
            cl.Times.Add(end);
            if (end >= cl.RepEnd) { cl.RepEnd = end; cl.RepSnapshot = snapshot; }
        }

        // Existing scenes, keyed by their on-device set, so we don't re-propose one the user already has.
        var existingByDevices = existingScenes
            .Select(SceneOnDeviceKey)
            .Where(k => k.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = new List<SceneCandidate>();
        foreach (var cl in clusters.Values)
        {
            if (cl.Times.Count < minSupport || cl.RepSnapshot is null) continue;

            var targets = BuildTargets(cl.RepSnapshot);
            if (targets.Count < minDevices) continue;

            if (existingByDevices.Contains(DeviceSetKey(targets.Select(t => t.DeviceId)))) continue; // already a scene

            var (minute, spread) = Schedule(cl.Times, options, siteClock);
            candidates.Add(new SceneCandidate(cl.Zone, targets, cl.Times.Count, minute, spread));
        }

        return candidates
            .OrderByDescending(c => c.Support)
            .ThenByDescending(c => c.Targets.Count)
            .ToList();
    }

    private sealed class Cluster
    {
        public Cluster(string zone) => Zone = zone;
        public string Zone { get; }
        public List<DateTime> Times { get; } = new();
        public DateTime RepEnd { get; set; } = DateTime.MinValue;
        public Dictionary<(string Dev, string Cap), object?>? RepSnapshot { get; set; }
    }

    // Seed each tracked capability with its value at the window's start: the first in-window event's OldValue
    // (the accurate pre-window state), falling back to the current read-model value for capabilities that never
    // change in the window (so a light that stayed off the whole time still reads "off").
    private static Dictionary<(string, string), object?> SeedState(
        IReadOnlyList<DbGatewayClient.EventLogEntry> events,
        IReadOnlyList<DbGatewayClient.DeviceSnapshot> devices,
        IReadOnlyDictionary<string, string> deviceZone,
        IReadOnlyDictionary<string, HashSet<string>> trackedCaps)
    {
        var seeds = new Dictionary<(string, string), object?>();
        foreach (var d in devices)
        {
            if (!deviceZone.ContainsKey(d.Id)) continue;
            foreach (var cap in trackedCaps[d.Id])
            {
                if (d.State.TryGetValue(cap, out var je)) seeds[(d.Id, cap)] = ToPrimitive(je);
                else if (string.Equals(cap, CapabilityIds.OnOff, StringComparison.OrdinalIgnoreCase)) seeds[(d.Id, cap)] = false;
            }
        }

        var seen = new HashSet<(string, string)>();
        foreach (var e in events)
        {
            if (!deviceZone.ContainsKey(e.DeviceId) || !trackedCaps[e.DeviceId].Contains(e.CapabilityId)) continue;
            var key = (e.DeviceId, e.CapabilityId);
            if (!seen.Add(key)) continue; // only the first event per capability carries the pre-window value
            var before = ToPrimitive(e.OldValue);
            if (before is not null) seeds[key] = before;
        }
        return seeds;
    }

    private static Dictionary<(string, string), object?> SnapshotZone(
        Dictionary<(string Dev, string Cap), object?> current,
        IReadOnlyDictionary<string, string> deviceZone,
        string zone) =>
        current
            .Where(kv => deviceZone.TryGetValue(kv.Key.Dev, out var z) && string.Equals(z, zone, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    // Devices whose on_off is truthy in the snapshot → device id → its captured caps.
    private static Dictionary<string, Dictionary<string, object?>> OnDevices(
        Dictionary<(string Dev, string Cap), object?> snapshot)
    {
        var byDev = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var kv in snapshot)
        {
            if (!byDev.TryGetValue(kv.Key.Dev, out var caps)) { caps = new(); byDev[kv.Key.Dev] = caps; }
            caps[kv.Key.Cap] = kv.Value;
        }
        return byDev
            .Where(d => d.Value.TryGetValue(CapabilityIds.OnOff, out var v) && IsOn(v))
            .ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
    }

    // A canonical, quantized key for a configuration — same on-devices at the same (coarse) levels ⇒ same scene.
    private static string ConfigKey(
        string zone, Dictionary<string, Dictionary<string, object?>> onDevices, IReadOnlyDictionary<string, double> steps)
    {
        var sb = new StringBuilder(zone).Append('|');
        foreach (var dev in onDevices.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(dev).Append('{');
            foreach (var cap in onDevices[dev].Keys.OrderBy(x => x, StringComparer.Ordinal))
                sb.Append(cap).Append('=').Append(FmtKey(Quantize(cap, onDevices[dev][cap], steps))).Append(';');
            sb.Append('}');
        }
        return sb.ToString();
    }

    // The scene's targets = each on-device with its captured writable state (real, unquantized values).
    private static List<SceneTargetDraft> BuildTargets(Dictionary<(string Dev, string Cap), object?> snapshot)
    {
        var on = OnDevices(snapshot);
        return on
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => new SceneTargetDraft(d.Key, d.Value.ToDictionary(c => c.Key, c => c.Value)))
            .ToList();
    }

    private static string SceneOnDeviceKey(Scene scene) =>
        DeviceSetKey(scene.Targets
            .Where(t => t.Set is not null && t.Set.TryGetValue(CapabilityIds.OnOff, out var v) && IsOn(v))
            .Select(t => t.DeviceId));

    private static string DeviceSetKey(IEnumerable<string> deviceIds) =>
        string.Join(",", deviceIds.Distinct().OrderBy(x => x, StringComparer.Ordinal));

    // Do the arrangement times cluster around one time of day? Then suggest a daily minute for a schedule rule.
    // "Time of day" is wall-clock time at the site: event timestamps are UTC, and a household that arranges the
    // living room at 21:00 local must get a rule that says (and fires at) 21:00, not the UTC hour behind it.
    private static (int? Minute, double Spread) Schedule(List<DateTime> times, AutomationOptions options, TimeZoneInfo siteZone)
    {
        if (times.Count < Math.Max(2, options.SceneScheduleMinSupport)) return (null, 0);

        var minutes = times.Select(t => (double)SiteMinuteOfDay(t, siteZone)).ToList();
        var mean = minutes.Average();
        var std = Math.Sqrt(minutes.Sum(m => (m - mean) * (m - mean)) / minutes.Count);
        if (std > options.SceneScheduleMaxSpreadMinutes) return (null, 0);
        return ((int)Math.Round(mean) % (24 * 60), Math.Round(std, 1));
    }

    /// <summary>Minute of day (0..1439) of a UTC event timestamp as read on the site's wall clock.</summary>
    internal static int SiteMinuteOfDay(DateTime utc, TimeZoneInfo siteZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), siteZone);
        return local.Hour * 60 + local.Minute;
    }

    private static bool IsOn(object? v) => v switch
    {
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        double d => d != 0,
        string s => bool.TryParse(s, out var b2) && b2,
        _ => false,
    };

    private static object? Quantize(string cap, object? v, IReadOnlyDictionary<string, double> steps)
    {
        if (v is bool) return v;
        double? d = v switch { long l => l, int i => i, double x => x, _ => (double?)null };
        if (d is null) return v; // strings/enums keyed verbatim
        var step = steps.TryGetValue(cap, out var s) && s > 0 ? s : 1.0;
        return Math.Round(d.Value / step) * step;
    }

    private static string FmtKey(object? v) => v switch
    {
        bool b => b ? "1" : "0",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        null => "",
        _ => v.ToString() ?? "",
    };

    private static object? ToPrimitive(JsonElement? value)
    {
        if (value is not { } e) return null;
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.String => e.GetString(),
            _ => null,
        };
    }
}
