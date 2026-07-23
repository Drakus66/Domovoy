// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Scenes;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Scene-schedule discovery (roadmap Epic 2F × 3B) — the pure core that notices an <b>existing</b> scene the
/// household keeps activating around the same time of day and proposes a daily rule that does it automatically.
/// Complements <see cref="SceneConfigurationMiner"/> (which invents the scene from raw state): once a scene
/// exists, this watches how it's used. It keys off the <c>scene</c> trigger-source stamped on every activation
/// (Epic 2F attribution), so a person pressing the scene tile is distinguishable from a rule already firing it —
/// only human activations are schedulable (anti-loop). Pure and unit-testable; the <see cref="DiscoveryEngine"/>
/// turns each result into a <c>Proposed</c> time-triggered <see cref="ActionType.Scene"/> rule (a person still
/// approves it).
/// </summary>
public static class SceneActivationMiner
{
    /// <summary>A scene the user activates at a consistent time of day → a candidate daily schedule.</summary>
    public sealed record SceneSchedule(string SceneId, string SceneName, int Minute, int Support, double Spread);

    // One tile-press fans out many device events sharing the scene id within a second or two; coalesce events of
    // the same scene closer than this into a single activation so we count presses, not per-device deltas.
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(120);

    public static List<SceneSchedule> Mine(
        IReadOnlyList<DbGatewayClient.EventLogEntry> events,
        IReadOnlyList<Scene> existingScenes,
        AutomationOptions options)
    {
        if (events.Count == 0 || existingScenes.Count == 0) return new();

        var nameById = existingScenes
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

        // Per scene: the distinct activation times (chronological events, coalesced by CoalesceWindow).
        var activations = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var lastAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        foreach (var e in events)
        {
            if (!string.Equals(e.TriggerSource, "scene", StringComparison.OrdinalIgnoreCase)) continue; // Epic 2F attribution
            var sceneId = e.TriggerId;
            if (string.IsNullOrEmpty(sceneId) || !nameById.ContainsKey(sceneId)) continue; // unknown/deleted scene

            if (lastAt.TryGetValue(sceneId, out var prev) && (e.Timestamp - prev) <= CoalesceWindow)
            {
                lastAt[sceneId] = e.Timestamp;
                continue; // same activation, already counted
            }
            lastAt[sceneId] = e.Timestamp;
            if (!activations.TryGetValue(sceneId, out var list)) { list = new(); activations[sceneId] = list; }
            list.Add(e.Timestamp);
        }

        var minSupport = Math.Max(2, options.SceneScheduleMinSupport);
        var results = new List<SceneSchedule>();
        foreach (var (sceneId, times) in activations)
        {
            if (times.Count < minSupport) continue;

            var minutes = times.Select(t => (double)(t.Hour * 60 + t.Minute)).ToList();
            var mean = minutes.Average();
            var std = Math.Sqrt(minutes.Sum(m => (m - mean) * (m - mean)) / minutes.Count);
            if (std > options.SceneScheduleMaxSpreadMinutes) continue; // activated at scattered times → not schedulable

            results.Add(new SceneSchedule(
                sceneId, nameById[sceneId], (int)Math.Round(mean) % (24 * 60), times.Count, Math.Round(std, 1)));
        }

        return results
            .OrderByDescending(r => r.Support)
            .ThenBy(r => r.Spread)
            .ToList();
    }
}
