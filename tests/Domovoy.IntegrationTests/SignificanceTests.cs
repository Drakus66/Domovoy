// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.Contracts.Narrative;
using Domovoy.Narrative;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>Offline tests for significance scoring + day consolidation (roadmap Epic 2N, Phase 2). Pure.</summary>
public class SignificanceTests
{
    private static readonly DateTime Noon = new(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);

    private static Beat B(string arch = "light", string cap = "on_off", Transition tr = Transition.On, string? devId = null) =>
        new() { ArchetypeKey = arch, CapabilityId = cap, Transition = tr, DeviceId = devId, Timestamp = Noon };

    private static Scene S(PersonaRole actor, DateTime started, string? mode = null, Beat[]? beats = null, Beat? cause = null) =>
        new()
        {
            Actor = actor,
            StartedAt = started,
            EndedAt = started,
            Mode = mode,
            Beats = (beats ?? new[] { B() }).ToList(),
            CauseBeat = cause,
        };

    private static SignificanceContext Ctx(Dictionary<string, int>? counts = null) =>
        new() { PatternCounts = counts ?? new Dictionary<string, int>(), TimeZone = TimeZoneInfo.Utc };

    [Fact]
    public void Human_action_outscores_automation_all_else_equal()
    {
        var human = SignificanceScorer.Score(S(PersonaRole.Residents, Noon), Ctx());
        var auto = SignificanceScorer.Score(S(PersonaRole.Spirit, Noon), Ctx());

        Assert.True(human.Score > auto.Score);
        Assert.Contains("human", human.Reasons);
    }

    [Fact]
    public void Vacation_mode_is_always_notable()
    {
        var (score, reasons) = SignificanceScorer.Score(S(PersonaRole.Spirit, Noon, mode: WellKnownModes.Vacation), Ctx());
        Assert.Contains("mode:Vacation", reasons);
        Assert.True(score >= SignificanceScorer.Base + SignificanceScorer.VacationWeight);
    }

    [Fact]
    public void Odd_hour_activity_is_flagged_anomalous()
    {
        var night = new DateTime(2026, 7, 9, 3, 0, 0, DateTimeKind.Utc);
        var (_, reasons) = SignificanceScorer.Score(S(PersonaRole.Spirit, night), Ctx());
        Assert.Contains("anomaly:odd-hour", reasons);
    }

    [Fact]
    public void Rare_pattern_outscores_a_common_one()
    {
        var beat = B("light", "on_off", Transition.On);
        var scene = S(PersonaRole.Spirit, Noon, beats: new[] { beat });

        var rare = SignificanceScorer.Score(scene, Ctx()); // count 0
        var common = SignificanceScorer.Score(scene, Ctx(new Dictionary<string, int> { ["light.on_off.On"] = 10 }));

        Assert.True(rare.Score > common.Score);
        Assert.Contains("rarity", rare.Reasons);
    }

    [Fact]
    public void Consolidate_returns_null_when_all_scenes_below_floor()
    {
        var scenes = new[] { Scored(S(PersonaRole.Spirit, Noon), 0.5), Scored(S(PersonaRole.Spirit, Noon), 0.5) };
        Assert.Null(DayConsolidator.Consolidate(DateOnly.FromDateTime(Noon), "UTC", scenes));
    }

    [Fact]
    public void Consolidate_returns_null_when_day_below_threshold()
    {
        var scenes = new[] { Scored(S(PersonaRole.Spirit, Noon), 2.0) }; // above floor but total 2.0 < 3.0
        Assert.Null(DayConsolidator.Consolidate(DateOnly.FromDateTime(Noon), "UTC", scenes));
    }

    [Fact]
    public void Consolidate_narrates_and_caps_and_orders_by_time()
    {
        var scenes = Enumerable.Range(0, 6)
            .Select(i => Rooted(Scored(S(PersonaRole.Spirit, Noon.AddMinutes(-i)), 2.0), $"manual:d{i}")) // distinct roots
            .ToArray();

        var day = DayConsolidator.Consolidate(DateOnly.FromDateTime(Noon), "UTC", scenes);

        Assert.NotNull(day);
        Assert.Equal(4, day!.Scenes.Count);                 // capped at MaxScenes
        Assert.True(day.Scenes.SequenceEqual(day.Scenes.OrderBy(s => s.StartedAt))); // chronological
        Assert.Equal(8.0, day.DayScore, 3);
    }

    [Fact]
    public void Consolidate_merges_same_signature_repeats_into_one_scene_with_count()
    {
        var scenes = Enumerable.Range(0, 4)
            .Select(i => Rooted(Scored(S(PersonaRole.Residents, Noon.AddHours(i)), 3.5), "manual:d1"))
            .ToArray();

        var day = DayConsolidator.Consolidate(DateOnly.FromDateTime(Noon), "UTC", scenes);

        Assert.NotNull(day);
        var scene = Assert.Single(day!.Scenes);             // ×4 clones collapse into the earliest occurrence
        Assert.Equal(4, scene.RepeatCount);
        Assert.Equal(Noon, scene.StartedAt);
        Assert.Equal(3.5, day.DayScore, 3);                 // best of the group, not the sum
    }

    [Fact]
    public void Consolidate_keeps_a_day_of_nothing_but_one_routine_repeat_silent()
    {
        // Four clones of a 2.0-scene merge into one 2.0-scene — below the 3.0 day threshold → silence.
        var scenes = Enumerable.Range(0, 4)
            .Select(i => Rooted(Scored(S(PersonaRole.Spirit, Noon.AddHours(i)), 2.0), "rule:r1"))
            .ToArray();

        Assert.Null(DayConsolidator.Consolidate(DateOnly.FromDateTime(Noon), "UTC", scenes));
    }

    private static Scene Scored(Scene s, double score)
    {
        s.Significance = score;
        return s;
    }

    private static Scene Rooted(Scene s, string root)
    {
        s.CausalRootKey = root;
        return s;
    }
}
