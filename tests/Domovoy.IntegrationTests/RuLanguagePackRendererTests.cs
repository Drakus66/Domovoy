// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;
using Domovoy.Narrative;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the deterministic Russian diary renderer (roadmap Epic 2N): gender/number/case
/// agreement, place prepositions, causal templates, anaphora and cooldown determinism. No Docker/Mongo —
/// pure template NLG over the built-in language pack.
/// </summary>
public class RuLanguagePackRendererTests
{
    private static readonly LanguagePack Pack = LanguagePack.LoadDefault("ru");
    private static readonly DateTime When = new(2026, 7, 9, 20, 0, 0, DateTimeKind.Utc);

    private static Beat MakeBeat(
        PersonaRole actor, string archetype, string capability, Transition transition,
        string? zoneName = null, string? zoneKind = null, string deviceRef = "") =>
        new()
        {
            Timestamp = When,
            Actor = actor,
            ArchetypeKey = archetype,
            CapabilityId = capability,
            Transition = transition,
            ZoneName = zoneName,
            ZoneKind = zoneKind,
            DeviceRef = deviceRef,
        };

    private static Scene MakeScene(Beat beat, string? cause = null) => new()
    {
        Id = "t",
        StartedAt = beat.Timestamp,
        EndedAt = beat.Timestamp,
        Actor = beat.Actor,
        Cause = cause,
        Beats = new List<Beat> { beat },
    };

    private static string Render(NarrativeState state, params Scene[] scenes)
    {
        var day = new DayStory { Date = DateOnly.FromDateTime(When), Scenes = scenes.ToList() };
        return new RuLanguagePackRenderer().Render(day, Pack, state).Paragraph;
    }

    [Fact]
    public void Residents_plural_light_on_in_named_room_agrees_and_places()
    {
        var scene = MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On, zoneName: "Гостиная"));
        Assert.Equal("Домочадцы зажгли свет в гостиной.", Render(new NarrativeState(), scene));
    }

    [Fact]
    public void Spirit_masculine_singular_verb_agreement()
    {
        var scene = MakeScene(MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.On));
        var text = Render(new NarrativeState(), scene);
        Assert.StartsWith("Домовой зажёг свет", text); // m_sg form «зажёг», not «зажгли»
    }

    [Fact]
    public void Residents_feminine_family_agreement_when_second_synonym_chosen()
    {
        // Seed the cursor so the next pick rotates to index 1 = «семья» (f, sg).
        var state = new NarrativeState();
        state.LastUsedIndex["persona:Residents"] = 0;
        var scene = MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On));
        var text = Render(state, scene);
        Assert.StartsWith("Семья зажгла свет", text); // f_sg «зажгла»
    }

    [Fact]
    public void Causal_effect_impersonal_cause_plus_spirit_on_veranda()
    {
        var scene = MakeScene(
            MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.On, zoneName: "Веранда"),
            cause: "стало темнеть");
        Assert.Equal("Стало темнеть — и домовой зажёг свет на веранде.", Render(new NarrativeState(), scene));
    }

    [Fact]
    public void Thermostat_heat_uses_device_place_form()
    {
        var scene = MakeScene(
            MakeBeat(PersonaRole.Spirit, "thermostat", "on_off", Transition.On),
            cause: "похолодало");
        Assert.Equal("Похолодало — и домовой поддал жару в котле.", Render(new NarrativeState(), scene));
    }

    [Fact]
    public void Impersonal_system_sensor_renders_standalone_phrase()
    {
        var beat = MakeBeat(PersonaRole.Impersonal, "sun", "is_dark", Transition.On);
        Assert.Equal("Стало темнеть.", Render(new NarrativeState(), MakeScene(beat)));
    }

    [Fact]
    public void Anaphora_second_mention_of_same_persona_uses_pronoun()
    {
        var s1 = MakeScene(MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.On));
        var s2 = MakeScene(MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.Off));
        var text = Render(new NarrativeState(), s1, s2);
        Assert.StartsWith("Домовой зажёг свет.", text);
        Assert.Contains("Он погасил свет", text); // pronoun, not repeated full name
    }

    [Fact]
    public void Rendering_is_deterministic_for_equal_state()
    {
        var scene = MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On, zoneName: "Кухня"));
        var a = Render(new NarrativeState(), MakeScene(scene.Beats[0]));
        var b = Render(new NarrativeState(), MakeScene(scene.Beats[0]));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Cooldown_rotates_persona_across_entries_deterministically()
    {
        // A shared state must rotate домочадцы → семья → домочадцы across successive day-entries.
        var state = new NarrativeState();
        var first = Render(state, MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On)));
        var second = Render(state, MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On)));
        var third = Render(state, MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On)));

        Assert.StartsWith("Домочадцы", first);
        Assert.StartsWith("Семья", second);
        Assert.StartsWith("Домочадцы", third);
    }
}
