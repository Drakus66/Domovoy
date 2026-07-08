// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;
using Domovoy.Narrative;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for scene coalescing + causal linkage (roadmap Epic 2N, Phase 1). Pure — no Mongo.
/// </summary>
public class SceneBuilderTests
{
    private static readonly DateTime T0 = new(2026, 7, 9, 20, 0, 0, DateTimeKind.Utc);

    private static Beat B(
        PersonaRole actor, string archetype, string capability, Transition transition, DateTime ts,
        string? ruleId = null, string? deviceId = null, string? zoneName = null) =>
        new()
        {
            Timestamp = ts,
            Actor = actor,
            ArchetypeKey = archetype,
            CapabilityId = capability,
            Transition = transition,
            RuleId = ruleId,
            DeviceId = deviceId,
            DeviceRef = deviceId ?? string.Empty,
            ZoneName = zoneName,
        };

    [Fact]
    public void Beats_sharing_a_rule_coalesce_into_one_scene()
    {
        var beats = new[]
        {
            B(PersonaRole.Spirit, "light", "on_off", Transition.On, T0, ruleId: "r1", deviceId: "d1"),
            B(PersonaRole.Spirit, "light", "on_off", Transition.On, T0.AddSeconds(5), ruleId: "r1", deviceId: "d2"),
        };

        var scenes = SceneBuilder.Build(beats, new SceneBuilderOptions { CoalesceWindow = TimeSpan.FromHours(1) });

        var scene = Assert.Single(scenes);
        Assert.Equal(2, scene.Beats.Count);
        Assert.Equal(PersonaRole.Spirit, scene.Actor);
        Assert.Equal("rule:r1", scene.CausalRootKey);
    }

    [Fact]
    public void Manual_actions_on_different_devices_stay_separate()
    {
        var beats = new[]
        {
            B(PersonaRole.Residents, "light", "on_off", Transition.On, T0, deviceId: "d1"),
            B(PersonaRole.Residents, "switch", "on_off", Transition.On, T0.AddSeconds(5), deviceId: "d2"),
        };

        var scenes = SceneBuilder.Build(beats);
        Assert.Equal(2, scenes.Count);
    }

    [Fact]
    public void Impersonal_transition_becomes_cause_of_following_rule_scene()
    {
        var beats = new[]
        {
            B(PersonaRole.Impersonal, "sun", "is_dark", Transition.On, T0), // "стало темнеть"
            B(PersonaRole.Spirit, "light", "on_off", Transition.On, T0.AddSeconds(30), ruleId: "r1", zoneName: "Веранда"),
        };

        var scenes = SceneBuilder.Build(beats);

        var scene = Assert.Single(scenes); // impersonal was consumed as the cause
        Assert.Equal(PersonaRole.Spirit, scene.Actor);
        Assert.NotNull(scene.CauseBeat);
        Assert.Equal("is_dark", scene.CauseBeat!.CapabilityId);

        var day = new DayStory { Date = DateOnly.FromDateTime(T0), Scenes = scenes };
        var text = new RuLanguagePackRenderer().Render(day, LanguagePack.LoadDefault("ru"), new NarrativeState()).Paragraph;
        Assert.Equal("Стало темнеть — и домовой зажёг свет на веранде.", text);
    }

    [Fact]
    public void Standalone_impersonal_without_a_following_rule_remains_a_scene()
    {
        var beats = new[] { B(PersonaRole.Impersonal, "sun", "is_dark", Transition.On, T0) };
        var scene = Assert.Single(SceneBuilder.Build(beats));
        Assert.Equal(PersonaRole.Impersonal, scene.Actor);
    }

    [Fact]
    public void Impersonal_outside_the_cause_window_is_not_adopted()
    {
        var beats = new[]
        {
            B(PersonaRole.Impersonal, "sun", "is_dark", Transition.On, T0),
            B(PersonaRole.Spirit, "light", "on_off", Transition.On, T0.AddMinutes(10), ruleId: "r1"),
        };

        var scenes = SceneBuilder.Build(beats, new SceneBuilderOptions { CauseWindow = TimeSpan.FromMinutes(5) });
        Assert.Equal(2, scenes.Count); // too far apart → both remain, no cause link
        Assert.All(scenes, s => Assert.Null(s.CauseBeat));
    }
}
