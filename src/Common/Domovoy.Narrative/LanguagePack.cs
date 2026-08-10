// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>
/// The data-driven rendering knowledge for one language (roadmap Epic 2N) — synonym pools with grammatical
/// markup, verb tables, place prepositions/cases, device nouns, impersonal phrases and sentence templates.
/// Adding a language is <b>authoring a pack</b>, not writing code: the built-in default is an embedded JSON
/// resource (<c>Packs/{locale}.json</c>); user <see cref="NarrativeEntity"/> overrides merge on top. We store
/// ready-made inflected forms (never generate morphology), so the renderer only looks up and agrees.
/// </summary>
public sealed class LanguagePack
{
    public string Locale { get; set; } = "ru";

    /// <summary>Pack revision — bumped on edits; stamped onto rendered <see cref="HomeStoryEntry"/>.</summary>
    public int PackVersion { get; set; }

    /// <summary>Actor synonym pools, keyed by <see cref="PersonaRole"/> name («Spirit»/«Residents»/…).</summary>
    public Dictionary<string, PersonaPool> Personas { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Verb tables, keyed by <c>{archetype}.{capability}.{Transition}</c> (+ <c>default.{Transition}</c> fallback).</summary>
    public Dictionary<string, VerbForms> Verbs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PlacesPack Places { get; set; } = new();

    public DevicesPack Devices { get; set; } = new();

    /// <summary>Impersonal phrases (System sensors, 2L), keyed by verb-key (<c>sun.is_dark.On</c>, <c>climate.colder</c>).</summary>
    public Dictionary<string, List<string>> Impersonal { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Time-of-day adverb pools («утром»/«вечером»…), keyed by bucket (<c>morning</c>/<c>day</c>/<c>evening</c>/<c>night</c>).</summary>
    public Dictionary<string, List<string>> TimesOfDay { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Count words for the repeat tail («дважды»/«трижды»), keyed by the count or <c>many</c>.</summary>
    public Dictionary<string, string> RepeatCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TemplatesPack Templates { get; set; } = new();

    public AnaphoraPack Anaphora { get; set; } = new();

    /// <summary>Genitive month names for date headings, index 0 = January.</summary>
    public List<string> MonthsGen { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Parse a pack from JSON text (used for the embedded default and any external pack).</summary>
    public static LanguagePack Parse(string json)
    {
        var pack = JsonSerializer.Deserialize<LanguagePack>(json, JsonOptions)
                   ?? throw new FormatException("Language pack JSON deserialized to null");
        // System.Text.Json rebuilds dictionaries with the default (case-sensitive) comparer, dropping the
        // OrdinalIgnoreCase set in the field initializers. Zone names / archetypes / verb keys must match
        // case-insensitively, so re-wrap every lookup after deserialization.
        pack.Personas = Ci(pack.Personas);
        pack.Verbs = Ci(pack.Verbs);
        pack.Impersonal = Ci(pack.Impersonal);
        pack.TimesOfDay = Ci(pack.TimesOfDay);
        pack.RepeatCounts = Ci(pack.RepeatCounts);
        pack.Places.ByZoneId = Ci(pack.Places.ByZoneId);
        pack.Places.ByZoneName = Ci(pack.Places.ByZoneName);
        pack.Places.ByZoneKind = Ci(pack.Places.ByZoneKind);
        pack.Devices.ByArchetype = Ci(pack.Devices.ByArchetype);
        pack.Devices.ByDeviceId = Ci(pack.Devices.ByDeviceId);
        return pack;
    }

    private static Dictionary<string, TValue> Ci<TValue>(Dictionary<string, TValue>? source) =>
        source is null
            ? new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TValue>(source, StringComparer.OrdinalIgnoreCase);

    /// <summary>Load the built-in default pack for a locale from the embedded <c>Packs/{locale}.json</c> resource.</summary>
    public static LanguagePack LoadDefault(string locale = "ru")
    {
        var asm = typeof(LanguagePack).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Packs.{locale}.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"No embedded language pack for locale '{locale}'");
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}

/// <summary>A rotated pool of actor synonyms plus a cooldown (min entries before a form may repeat).</summary>
public sealed class PersonaPool
{
    public List<SynonymForm> Pool { get; set; } = new();
    public int Cooldown { get; set; } = 1;
}

/// <summary>
/// The three Russian past-tense forms for a verb (agree by actor gender/number only) plus optional
/// alternates rotated with cooldown, and hints on how the device/zone attach to the clause.
/// </summary>
public sealed class VerbForms
{
    [JsonPropertyName("m_sg")] public string? MSg { get; set; }
    [JsonPropertyName("f_sg")] public string? FSg { get; set; }
    [JsonPropertyName("pl")] public string? Pl { get; set; }

    /// <summary>Alternate lemmas (each carrying the 3 forms) — cooldown-rotated to avoid repetition.</summary>
    public List<VerbForms>? Synonyms { get; set; }

    /// <summary>
    /// How the device renders: <c>device</c> (nominative object, «зажгли свет») or <c>none</c>.
    /// Null on a synonym means «inherit from the base lemma»; null on a base lemma means <c>device</c>.
    /// </summary>
    public string? Object { get; set; }

    /// <summary>
    /// Where the location comes from: <c>zone</c> («в гостиной»), <c>device</c> («в котле») or <c>none</c>.
    /// Null on a synonym means «inherit from the base lemma»; null on a base lemma means <c>zone</c>.
    /// </summary>
    public string? Place { get; set; }

    /// <summary>Pick the form for an actor's gender/number.</summary>
    public string Form(string? gender, string? number)
    {
        if (string.Equals(number, "pl", StringComparison.OrdinalIgnoreCase)) return Pl ?? MSg ?? string.Empty;
        if (string.Equals(gender, "f", StringComparison.OrdinalIgnoreCase)) return FSg ?? MSg ?? string.Empty;
        return MSg ?? string.Empty;
    }
}

/// <summary>
/// Place forms, resolved most-specific first: a per-zone-id override (owner-named) wins over a
/// common-room-name form (declined forms for «гостиная»/«веранда»/… shipped in the pack) which in turn
/// wins over a coarse per-zone-kind fallback (preposition only → the place is omitted when we can't
/// decline the name, keeping the sentence grammatical).
/// </summary>
public sealed class PlacesPack
{
    public Dictionary<string, SynonymForm> ByZoneId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SynonymForm> ByZoneName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SynonymForm> ByZoneKind { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Device nouns: per-device overrides win over a per-archetype default.</summary>
public sealed class DevicesPack
{
    public Dictionary<string, SynonymForm> ByArchetype { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SynonymForm> ByDeviceId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Sentence templates — slot order and connectives per locale.</summary>
public sealed class TemplatesPack
{
    /// <summary>«{Actor} {Verb} {Object}{Place}» — a plain actor action.</summary>
    public string ActorAction { get; set; } = "{Actor} {Verb} {Object}{Place}";

    /// <summary>«{Cause} — и {Actor} {Verb} {Object}{Place}» — a caused effect (dash form).</summary>
    public string CausalEffect { get; set; } = "{Cause} — и {Actor} {Verb} {Object}{Place}";

    /// <summary>«{Cause}, и {Actor} {Verb} {Object}{Place}» — a caused effect (comma form).</summary>
    public string CausalComma { get; set; } = "{Cause}, и {Actor} {Verb} {Object}{Place}";

    /// <summary>Tail appended when a merged scene repeated during the day: « — и так {Count} за день».</summary>
    public string RepeatSuffix { get; set; } = " — и так {Count} за день";
}

/// <summary>Anaphora policy: introduce with a full name, then pronominalize within the day-entry.</summary>
public sealed class AnaphoraPack
{
    public string FirstMention { get; set; } = "full";
    public string LaterMention { get; set; } = "pronoun";
    public bool ScopePerEntry { get; set; } = true;
}
