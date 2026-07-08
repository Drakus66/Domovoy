// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// BSON round-trip of a <see cref="HomeStoryEntry"/> (roadmap Epic 2N) — the one genuinely risky persistence
/// detail. The entry stores the language-neutral scene IR (<see cref="Scene"/> → <see cref="Beat"/>) with
/// <c>object?</c> old/new values and enum fields; this verifies it survives Mongo serialization so a stored
/// day can be re-rendered later. Pure — no infrastructure (mirrors the DashboardDocument round-trip).
/// </summary>
public sealed class HomeStoryPersistenceTests
{
    [Fact]
    public void HomeStoryEntry_WithSceneIr_RoundTripsThroughBson()
    {
        BsonTestSerializers.EnsureRegistered();

        var entry = new HomeStoryEntry
        {
            Id = "story-2026-07-09",
            Date = new DateTime(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc),
            Locale = "ru",
            Paragraph = "Стало темнеть — и домовой зажёг свет на веранде.",
            DayScore = 4.5,
            Tier = 0,
            RendererTier = "deterministic",
            PackVersion = 1,
            Scenes = new List<Scene>
            {
                new()
                {
                    Id = "s1",
                    Actor = PersonaRole.Spirit,
                    CausalRootKey = "rule:r1",
                    Mode = "Night",
                    Significance = 3.0,
                    SignificanceReasons = new List<string> { "causal", "rarity" },
                    CauseBeat = new Beat
                    {
                        Actor = PersonaRole.Impersonal, ArchetypeKey = "sun",
                        CapabilityId = "is_dark", Transition = Transition.On,
                        OldValue = false, NewValue = true,
                    },
                    Beats = new List<Beat>
                    {
                        new()
                        {
                            Actor = PersonaRole.Spirit, ArchetypeKey = "light", CapabilityId = "on_off",
                            Transition = Transition.On, DeviceId = "d1", DeviceRef = "d1",
                            ZoneName = "Веранда", ZoneKind = "outdoor",
                            OldValue = false, NewValue = true, RuleId = "r1", Mode = "Night",
                        },
                    },
                },
            },
        };

        var restored = BsonSerializer.Deserialize<HomeStoryEntry>(entry.ToBsonDocument());

        Assert.Equal("story-2026-07-09", restored.Id);
        Assert.Equal(entry.Paragraph, restored.Paragraph);
        Assert.Equal(4.5, restored.DayScore, 3);

        var scene = Assert.Single(restored.Scenes);
        Assert.Equal(PersonaRole.Spirit, scene.Actor);
        Assert.Equal(new[] { "causal", "rarity" }, scene.SignificanceReasons);

        Assert.NotNull(scene.CauseBeat);
        Assert.Equal(PersonaRole.Impersonal, scene.CauseBeat!.Actor);
        Assert.Equal("is_dark", scene.CauseBeat.CapabilityId);
        Assert.Equal(Transition.On, scene.CauseBeat.Transition);

        var beat = Assert.Single(scene.Beats);
        Assert.Equal("light", beat.ArchetypeKey);
        Assert.Equal(Transition.On, beat.Transition);
        Assert.Equal("Веранда", beat.ZoneName);
        Assert.Equal(true, Assert.IsType<bool>(beat.NewValue));
        Assert.Equal(false, Assert.IsType<bool>(beat.OldValue));
    }
}
