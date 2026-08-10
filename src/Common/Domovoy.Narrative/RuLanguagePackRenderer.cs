// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text.RegularExpressions;

using Domovoy.Contracts.Narrative;
using Domovoy.Contracts.Home;

namespace Domovoy.Narrative;

/// <summary>
/// The deterministic, offline Russian renderer (roadmap Epic 2N) — the mandatory default behind the
/// <see cref="INarrativeRenderer"/> seam. It fills sentence templates from the language pack: the chosen
/// actor synonym's gender/number selects the past-tense verb column and the anaphora pronoun; places and
/// device nouns use stored inflected forms (no morphology generation). Synonym choice is a deterministic
/// cooldown/LRU over <see cref="NarrativeState"/> — never randomness — so replay is exact.
/// Sentence-quality invariants: a verb that requires a device object is never emitted bare (the scene is
/// skipped instead), the third+ mention of an actor drops the subject (Russian pro-drop), a time-of-day
/// adverb opens a sentence when the narration moves to another part of the day, and a merged repeated
/// scene gets an «— и так несколько раз за день» tail.
/// </summary>
public sealed class RuLanguagePackRenderer : INarrativeRenderer
{
    public string Locale => "ru";

    public RenderTier Tier => RenderTier.Deterministic;

    public RenderedStory Render(DayStory day, LanguagePack pack, NarrativeState state)
    {
        state.EntryCounter++; // advance the cooldown clock once per day-entry
        var tz = ResolveTimeZone(day.TimeZoneId);
        var anaphora = new Dictionary<PersonaRole, ActorMention>();
        var mentions = new Dictionary<PersonaRole, int>();
        var sentences = new List<string>();
        string? prevBucket = null;

        foreach (var scene in day.Scenes)
        {
            var bucket = TimeOfDayBucket(scene.StartedAt, tz);
            var adverbBucket = sentences.Count > 0 && !string.Equals(bucket, prevBucket, StringComparison.Ordinal)
                ? bucket
                : null;

            var s = RenderScene(scene, pack, state, anaphora, mentions, adverbBucket);
            if (string.IsNullOrWhiteSpace(s)) continue;

            sentences.Add(EnsureSentence(Capitalize(Clean(s))));
            prevBucket = bucket;
        }

        state.UpdatedAt = DateTime.UtcNow;
        return new RenderedStory(string.Join(" ", sentences), "deterministic");
    }

    private string RenderScene(
        Scene scene, LanguagePack pack, NarrativeState state,
        Dictionary<PersonaRole, ActorMention> anaphora, Dictionary<PersonaRole, int> mentions,
        string? adverbBucket)
    {
        var beat = scene.Beats.FirstOrDefault();
        if (beat is null) return string.Empty;

        string sentence;
        if (scene.Actor == PersonaRole.Impersonal)
        {
            // Impersonal scene (System sensors, 2L): just the impersonal phrase, no actor/verb.
            sentence = PickImpersonal(beat, pack, state);
            if (string.IsNullOrWhiteSpace(sentence)) return string.Empty;
        }
        else
        {
            var lemma = ResolveVerbLemma(beat, pack, state);
            if (lemma is null) return string.Empty;
            var (forms, objectMode, placeMode) = lemma.Value;

            var obj = string.Equals(objectMode, "device", StringComparison.OrdinalIgnoreCase)
                ? ResolveDevice(beat, pack)?.Text ?? string.Empty
                : string.Empty;
            // Invariant: a verb that needs its device object never goes out bare («Домочадцы изменили.»).
            if (string.Equals(objectMode, "device", StringComparison.OrdinalIgnoreCase) && obj.Length == 0)
                return string.Empty;

            var place = ResolvePlace(beat, placeMode, pack);

            var actor = ResolveActor(scene.Actor, pack, state, anaphora, mentions);
            var verb = forms.Form(actor.Gender, actor.Number);
            if (string.IsNullOrWhiteSpace(verb)) return string.Empty;

            // Cause: a pre-resolved phrase wins; otherwise localize the language-neutral cause beat here.
            var cause = scene.Cause;
            if (string.IsNullOrWhiteSpace(cause) && scene.CauseBeat is not null)
                cause = PickImpersonal(scene.CauseBeat, pack, state);

            sentence = !string.IsNullOrWhiteSpace(cause)
                ? Fill(pack.Templates.CausalEffect,
                    ("Cause", cause!), ("Actor", actor.Text), ("Verb", verb), ("Object", obj), ("Place", place))
                : Fill(pack.Templates.ActorAction,
                    ("Actor", actor.Text), ("Verb", verb), ("Object", obj), ("Place", place));
        }

        if (adverbBucket is not null)
        {
            var adverb = PickTimeAdverb(adverbBucket, pack, state);
            if (adverb.Length > 0) sentence = $"{adverb} {sentence}";
        }

        if (scene.RepeatCount > 1)
            sentence += RepeatTail(scene.RepeatCount, pack);

        return sentence;
    }

    private static ActorMention ResolveActor(
        PersonaRole role, LanguagePack pack, NarrativeState state,
        Dictionary<PersonaRole, ActorMention> anaphora, Dictionary<PersonaRole, int> mentions)
    {
        var mention = mentions.TryGetValue(role, out var n) ? n : 0;
        mentions[role] = mention + 1;

        // Second mention within the day-entry → pronoun; third+ → null subject (pro-drop), both keeping
        // the introduced form's agreement.
        if (mention > 0 && pack.Anaphora.LaterMention == "pronoun" && anaphora.TryGetValue(role, out var prev))
            return mention == 1
                ? prev with { Text = prev.Pronoun ?? prev.Text }
                : prev with { Text = string.Empty };

        if (!pack.Personas.TryGetValue(role.ToString(), out var poolDef) || poolDef.Pool.Count == 0)
        {
            var fb = new ActorMention(role.ToString(), null, null, null);
            anaphora[role] = fb;
            return fb;
        }

        var idx = PickIndex($"persona:{role}", poolDef.Pool.Count, poolDef.Cooldown, state);
        var syn = poolDef.Pool[idx];
        var introduced = new ActorMention(syn.SubjectCase ?? syn.Text, syn.Gender, syn.Number, syn.Pronoun);
        anaphora[role] = introduced;
        return introduced;
    }

    /// <summary>Pick the verb lemma (base or rotated synonym) plus the effective object/place modes.
    /// A synonym may override how the device/zone attach; unset falls back to the base lemma.</summary>
    private static (VerbForms Forms, string ObjectMode, string PlaceMode)? ResolveVerbLemma(
        Beat beat, LanguagePack pack, NarrativeState state)
    {
        var key = VerbKey(beat);
        if (!pack.Verbs.TryGetValue(key, out var forms))
            pack.Verbs.TryGetValue($"default.{beat.Transition}", out forms);
        if (forms is null) return null;

        var lemmas = new List<VerbForms> { forms };
        if (forms.Synonyms is { Count: > 0 }) lemmas.AddRange(forms.Synonyms);
        var idx = PickIndex($"verb:{key}", lemmas.Count, 1, state);
        var chosen = lemmas[idx];

        return (chosen, chosen.Object ?? forms.Object ?? "device", chosen.Place ?? forms.Place ?? "zone");
    }

    private static string ResolvePlace(Beat beat, string placeMode, LanguagePack pack)
    {
        var place = placeMode.ToLowerInvariant() switch
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

    private static string PickTimeAdverb(string bucket, LanguagePack pack, NarrativeState state)
    {
        if (!pack.TimesOfDay.TryGetValue(bucket, out var adverbs) || adverbs.Count == 0) return string.Empty;
        var idx = PickIndex($"time:{bucket}", adverbs.Count, 1, state);
        return adverbs[idx];
    }

    private static string RepeatTail(int count, LanguagePack pack)
    {
        var template = pack.Templates.RepeatSuffix;
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (!pack.RepeatCounts.TryGetValue(count.ToString(CultureInfo.InvariantCulture), out var word) &&
            !pack.RepeatCounts.TryGetValue("many", out word))
            return string.Empty;
        return template.Replace("{Count}", word);
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

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId) => SiteTimeZone.Resolve(timeZoneId);

    private static string TimeOfDayBucket(DateTime utc, TimeZoneInfo tz)
    {
        var instant = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(instant, tz);
        return local.Hour switch
        {
            >= 5 and < 11 => "morning",
            >= 11 and < 17 => "day",
            >= 17 and < 23 => "evening",
            _ => "night",
        };
    }

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
}
