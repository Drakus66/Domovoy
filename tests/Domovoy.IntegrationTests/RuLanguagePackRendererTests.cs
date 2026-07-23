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

    [Fact]
    public void Scene_with_unresolvable_device_object_is_skipped_not_rendered_bare()
    {
        // default.Set needs its device object; archetype «gizmo» has no noun → no «Домочадцы изменили.»
        var scene = MakeScene(MakeBeat(PersonaRole.Residents, "gizmo", "mode", Transition.Set));
        Assert.Equal("", Render(new NarrativeState(), scene));
    }

    [Fact]
    public void Unknown_archetype_falls_back_to_generic_device_noun()
    {
        var scene = MakeScene(MakeBeat(PersonaRole.Residents, "unknown", "mode", Transition.Set, zoneName: "Гостиная"));
        Assert.Equal("Домочадцы изменили устройство в гостиной.", Render(new NarrativeState(), scene));
    }

    [Fact]
    public void Verb_synonym_object_override_is_honoured()
    {
        // Rotate valve.on_off.On to its synonym «включил», which overrides object=none → object=device.
        var state = new NarrativeState();
        state.LastUsedIndex["verb:valve.on_off.On"] = 0;
        var scene = MakeScene(MakeBeat(PersonaRole.Residents, "valve", "on_off", Transition.On, zoneName: "Сад"));
        Assert.Equal("Домочадцы включили полив в саду.", Render(state, scene));
    }

    [Fact]
    public void Third_mention_of_same_persona_drops_the_subject()
    {
        var s1 = MakeScene(MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.On));
        var s2 = MakeScene(MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.Off));
        var s3 = MakeScene(MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.On));
        var text = Render(new NarrativeState(), s1, s2, s3);
        // full name → pronoun → null subject; the On-verb pool also rotates (зажёг → включил)
        Assert.Equal("Домовой зажёг свет. Он погасил свет. Включил свет.", text);
    }

    [Fact]
    public void Time_of_day_adverb_opens_a_sentence_when_the_bucket_changes()
    {
        var morning = MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.On);
        morning.Timestamp = new DateTime(2026, 7, 9, 7, 0, 0, DateTimeKind.Utc);
        var evening = MakeBeat(PersonaRole.Spirit, "light", "on_off", Transition.Off);
        evening.Timestamp = new DateTime(2026, 7, 9, 20, 0, 0, DateTimeKind.Utc);

        var text = Render(new NarrativeState(), MakeScene(morning), MakeScene(evening));

        Assert.Equal("Домовой зажёг свет. Вечером он погасил свет.", text);
    }

    [Fact]
    public void Merged_repeat_scene_gets_a_count_tail()
    {
        var twice = MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On, zoneName: "Кухня"));
        twice.RepeatCount = 2;
        Assert.Equal("Домочадцы зажгли свет на кухне — и так дважды за день.", Render(new NarrativeState(), twice));

        var many = MakeScene(MakeBeat(PersonaRole.Residents, "light", "on_off", Transition.On, zoneName: "Кухня"));
        many.RepeatCount = 5;
        Assert.EndsWith("— и так несколько раз за день.", Render(new NarrativeState(), many));
    }
}
