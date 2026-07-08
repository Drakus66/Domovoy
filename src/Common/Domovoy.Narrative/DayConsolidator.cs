// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>Knobs for day consolidation (roadmap Epic 2N, Phase 2).</summary>
public sealed class DayConsolidatorOptions
{
    /// <summary>At most this many scenes are narrated per day (the «2–4 бита» rule).</summary>
    public int MaxScenes { get; set; } = 4;

    /// <summary>A scene must score at least this to be eligible for narration.</summary>
    public double SceneFloor { get; set; } = 1.5;

    /// <summary>The day is narrated only if the chosen scenes' total score reaches this — else silence.</summary>
    public double DayThreshold { get; set; } = 3.0;
}

/// <summary>
/// Consolidates a day's scored scenes into a narrated <see cref="DayStory"/> (roadmap Epic 2N, Phase 2):
/// keep the most significant scenes above the floor (capped at <see cref="DayConsolidatorOptions.MaxScenes"/>),
/// and narrate the day only if the total clears the threshold — otherwise return <c>null</c> («молчание —
/// фича», the day is silently skipped). Chosen scenes are ordered by time for reading. Pure and deterministic.
/// </summary>
public static class DayConsolidator
{
    public static DayStory? Consolidate(
        DateOnly date, string timeZoneId, IReadOnlyList<Scene> scoredScenes, DayConsolidatorOptions? options = null)
    {
        options ??= new DayConsolidatorOptions();

        var eligible = scoredScenes
            .Where(s => s.Significance >= options.SceneFloor)
            .OrderByDescending(s => s.Significance)
            .ThenBy(s => s.StartedAt)
            .Take(options.MaxScenes)
            .ToList();

        if (eligible.Count == 0) return null;

        var dayScore = eligible.Sum(s => s.Significance);
        if (dayScore < options.DayThreshold) return null; // silence

        return new DayStory
        {
            Date = date,
            TimeZoneId = timeZoneId,
            Scenes = eligible.OrderBy(s => s.StartedAt).ToList(),
            DayScore = dayScore,
        };
    }
}
