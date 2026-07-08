// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Narrative;

/// <summary>
/// A user-editable synonym pool with grammatical markup (roadmap Epic 2N), stored in the
/// <c>narrative_entities</c> collection and <b>merged over</b> the built-in default language pack. This is
/// the personalization surface: a household can rename the house spirit, the collective «домочадцы», a
/// zone's spoken form, or a device's noun — without touching code. Absent an override, the default pack wins.
/// </summary>
public class NarrativeEntity
{
    /// <summary>Stable id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Language this override applies to (e.g. <c>ru</c>).</summary>
    public string Locale { get; set; } = "ru";

    /// <summary>What is being overridden (see <see cref="NarrativeEntityKinds"/>).</summary>
    public string Kind { get; set; } = NarrativeEntityKinds.Persona;

    /// <summary>
    /// Which pool: for <c>persona</c> the <see cref="PersonaRole"/> name («Spirit»/«Residents»); for
    /// <c>place</c> a zone id; for <c>device</c> a device id or archetype token.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The synonym pool (rotated with cooldown at render time).</summary>
    public List<SynonymForm> Synonyms { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A single synonym with the grammatical markup that lets the deterministic renderer agree verbs and
/// pronouns without a morphology library — we store ready-made forms, never generate them (roadmap Epic 2N).
/// Mirrors the JSON language-pack entry so DB overrides merge cleanly over the built-in pack.
/// </summary>
public class SynonymForm
{
    /// <summary>The canonical surface form (nominative).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Grammatical gender: <c>m</c> | <c>f</c> | <c>n</c> — selects the verb column.</summary>
    public string? Gender { get; set; }

    /// <summary>Number: <c>sg</c> | <c>pl</c> — «семья» (f sg) vs «домочадцы» (pl).</summary>
    public string? Number { get; set; }

    /// <summary>Animacy: <c>anim</c> | <c>inan</c>.</summary>
    public string? Animacy { get; set; }

    /// <summary>Preposition for a place («в»/«на»/«у») — stored, not inferred.</summary>
    public string? Prep { get; set; }

    /// <summary>Prepositional-case form of a place («гостиной», «веранде»).</summary>
    public string? LocForm { get; set; }

    /// <summary>Accusative-case form where a verb takes a direct object («веранду»).</summary>
    public string? AccForm { get; set; }

    /// <summary>Subject-case (nominative) form of an actor persona, if it differs from <see cref="Text"/>.</summary>
    public string? SubjectCase { get; set; }

    /// <summary>Pronoun for anaphora after first mention («он»/«она»/«они»).</summary>
    public string? Pronoun { get; set; }
}

/// <summary>Well-known narrative-entity kinds (open set).</summary>
public static class NarrativeEntityKinds
{
    /// <summary>An actor persona pool (narrator/residents), keyed by <see cref="PersonaRole"/> name.</summary>
    public const string Persona = "persona";

    /// <summary>A place pool, keyed by zone id.</summary>
    public const string Place = "place";

    /// <summary>A device pool, keyed by device id or archetype token.</summary>
    public const string Device = "device";

    public static readonly IReadOnlyList<string> All = new[] { Persona, Place, Device };
}
