// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text.RegularExpressions;

using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>
/// The deterministic, offline Russian renderer (roadmap Epic 2N) — the mandatory default behind the
/// <see cref="INarrativeRenderer"/> seam. It fills sentence templates from the language pack: the chosen
/// actor synonym's gender/number selects the past-tense verb column and the anaphora pronoun; places and
/// device nouns use stored inflected forms (no morphology generation). Synonym choice is a deterministic
/// cooldown/LRU over <see cref="NarrativeState"/> — never randomness — so replay is exact.
/// </summary>
public sealed class RuLanguagePackRenderer : INarrativeRenderer
{
    public string Locale => "ru";

    public RenderTier Tier => RenderTier.Deterministic;

    public RenderedStory Render(DayStory day, LanguagePack pack, NarrativeState state)
    {
        state.EntryCounter++; // advance the cooldown clock once per day-entry
        var anaphora = new Dictionary<PersonaRole, ActorMention>();
        var sentences = new List<string>();

        foreach (var scene in day.Scenes)
        {
            var s = RenderScene(scene, pack, state, anaphora);
            if (!string.IsNullOrWhiteSpace(s)) sentences.Add(EnsureSentence(Clean(s)));
        }

        state.UpdatedAt = DateTime.UtcNow;
        return new RenderedStory(string.Join(" ", sentences), "deterministic");
    }

    private string RenderScene(Scene scene, LanguagePack pack, NarrativeState state, Dictionary<PersonaRole, ActorMention> anaphora)
    {
        var beat = scene.Beats.FirstOrDefault();
        if (beat is null) return string.Empty;

        // Impersonal scene (System sensors, 2L): just the impersonal phrase, no actor/verb.
        if (scene.Actor == PersonaRole.Impersonal)
            return Capitalize(PickImpersonal(beat, pack, state));

        var actor = ResolveActor(scene.Actor, pack, state, anaphora);
        var verb = ResolveVerb(beat, actor, pack, state);
        var obj = ResolveObject(beat, verb, pack);
        var place = ResolvePlace(beat, verb, pack);

        if (!string.IsNullOrWhiteSpace(scene.Cause))
            return Fill(pack.Templates.CausalEffect,
                ("Cause", Capitalize(scene.Cause!)), ("Actor", actor.Text),
                ("Verb", verb.Text), ("Object", obj), ("Place", place));

        return Fill(pack.Templates.ActorAction,
            ("Actor", Capitalize(actor.Text)), ("Verb", verb.Text), ("Object", obj), ("Place", place));
    }

    private static ActorMention ResolveActor(
        PersonaRole role, LanguagePack pack, NarrativeState state, Dictionary<PersonaRole, ActorMention> anaphora)
    {
        // Later mention within the same day-entry → pronoun (keeps the introduced form's agreement).
        if (anaphora.TryGetValue(role, out var prev) && pack.Anaphora.LaterMention == "pronoun")
            return prev with { Text = prev.Pronoun ?? prev.Text };

        if (!pack.Personas.TryGetValue(role.ToString(), out var poolDef) || poolDef.Pool.Count == 0)
        {
            var fb = new ActorMention(role.ToString(), null, null, null);
            anaphora[role] = fb;
            return fb;
        }

        var idx = PickIndex($"persona:{role}", poolDef.Pool.Count, poolDef.Cooldown, state);
        var syn = poolDef.Pool[idx];
        var mention = new ActorMention(syn.SubjectCase ?? syn.Text, syn.Gender, syn.Number, syn.Pronoun);
        anaphora[role] = mention;
        return mention;
    }

    private static VerbChoice ResolveVerb(Beat beat, ActorMention actor, LanguagePack pack, NarrativeState state)
    {
        var key = VerbKey(beat);
        if (!pack.Verbs.TryGetValue(key, out var forms))
            pack.Verbs.TryGetValue($"default.{beat.Transition}", out forms);
        if (forms is null)
            return new VerbChoice(string.Empty, "device", "zone");

        var lemmas = new List<VerbForms> { forms };
        if (forms.Synonyms is { Count: > 0 }) lemmas.AddRange(forms.Synonyms);
        var idx = PickIndex($"verb:{key}", lemmas.Count, 1, state);
        var text = lemmas[idx].Form(actor.Gender, actor.Number);
        return new VerbChoice(text, forms.Object, forms.Place);
    }

    private static string ResolveObject(Beat beat, VerbChoice verb, LanguagePack pack)
    {
        if (!string.Equals(verb.ObjectMode, "device", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return ResolveDevice(beat, pack)?.Text ?? string.Empty;
    }

    private static string ResolvePlace(Beat beat, VerbChoice verb, LanguagePack pack)
    {
        var place = verb.PlaceMode.ToLowerInvariant() switch
        {
            "device" => ResolveDevice(beat, pack),
            "zone" => ResolveZone(beat, pack),
            _ => null,
        };
        if (place?.Prep is null || string.IsNullOrEmpty(place.LocForm)) return string.Empty;
        return $" {place.Prep} {place.LocForm}";
    }

    private static SynonymForm? ResolveDevice(Beat beat, LanguagePack pack)
    {
        if (!string.IsNullOrEmpty(beat.DeviceRef) && pack.Devices.ByDeviceId.TryGetValue(beat.DeviceRef, out var d))
            return d;
        if (!string.IsNullOrEmpty(beat.ArchetypeKey) && pack.Devices.ByArchetype.TryGetValue(beat.ArchetypeKey, out var a))
            return a;
        return null;
    }

    private static SynonymForm? ResolveZone(Beat beat, LanguagePack pack)
    {
        if (!string.IsNullOrEmpty(beat.ZoneId) && pack.Places.ByZoneId.TryGetValue(beat.ZoneId, out var z))
            return z;
        if (!string.IsNullOrEmpty(beat.ZoneName) && pack.Places.ByZoneName.TryGetValue(beat.ZoneName!.Trim(), out var n))
            return n; // declined form for a common room name shipped in the pack
        if (!string.IsNullOrEmpty(beat.ZoneKind) && pack.Places.ByZoneKind.TryGetValue(beat.ZoneKind!, out var k))
            return k; // coarse fallback: may lack a loc_form → place omitted
        return null;
    }

    private static string PickImpersonal(Beat beat, LanguagePack pack, NarrativeState state)
    {
        var key = VerbKey(beat);
        if (!pack.Impersonal.TryGetValue(key, out var phrases) || phrases.Count == 0) return string.Empty;
        var idx = PickIndex($"impersonal:{key}", phrases.Count, 1, state);
        return phrases[idx];
    }

    /// <summary>Deterministic cooldown/LRU: round-robin the pool, advancing the persisted cursor (no RNG).</summary>
    private static int PickIndex(string poolKey, int count, int cooldown, NarrativeState state)
    {
        if (count <= 1) return 0;
        var next = state.LastUsedIndex.TryGetValue(poolKey, out var last) ? (last + 1) % count : 0;
        state.LastUsedIndex[poolKey] = next;
        state.LastUsedEntryNo[poolKey] = state.EntryCounter;
        return next;
    }

    private static string VerbKey(Beat beat) => $"{beat.ArchetypeKey}.{beat.CapabilityId}.{beat.Transition}";

    private static string Fill(string template, params (string Key, string Value)[] slots)
    {
        var result = template;
        foreach (var (key, value) in slots)
            result = result.Replace("{" + key + "}", value);
        return result;
    }

    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

    private static string Clean(string s) => MultiSpace.Replace(s, " ").Replace(" ,", ",").Trim();

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0], CultureInfo.GetCultureInfo("ru-RU")) + s.Substring(1);
    }

    private static string EnsureSentence(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var last = s[^1];
        return last is '.' or '!' or '?' or '…' ? s : s + ".";
    }

    /// <summary>An actor's rendered surface plus the grammar needed to agree the verb and pick the pronoun.</summary>
    private sealed record ActorMention(string Text, string? Gender, string? Number, string? Pronoun);

    /// <summary>A chosen verb surface plus how the device/zone attach to the clause.</summary>
    private sealed record VerbChoice(string Text, string ObjectMode, string PlaceMode);
}
