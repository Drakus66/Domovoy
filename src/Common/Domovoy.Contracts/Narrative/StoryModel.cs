// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Narrative;

/// <summary>
/// Who acted in a story beat (roadmap Epic 2N) — the language-neutral persona derived from the event's
/// trigger source. The renderer maps each role to a synonym pool: <see cref="Residents"/>→«домочадцы»,
/// <see cref="Spirit"/>→«дух дома»/«домовой», <see cref="SpiritJudging"/>→ML-governor (2B) with a judging
/// tint, <see cref="Impersonal"/>→«стало темнеть» (System sensors, 2L). Kept language-neutral so the same
/// IR can be re-rendered in any locale.
/// </summary>
public enum PersonaRole
{
    /// <summary>Manual/user action — the collective «домочадцы» (no per-person identity in Phase 2).</summary>
    Residents,

    /// <summary>Rule/block action — «дух дома»/«домовой».</summary>
    Spirit,

    /// <summary>ML-governor action (Epic 2B) — the spirit with a judging tint.</summary>
    SpiritJudging,

    /// <summary>System virtual sensor (Epic 2L) — impersonal «стало темнеть».</summary>
    Impersonal,
}

/// <summary>The kind of change a capability underwent — drives verb selection (language-neutral).</summary>
public enum Transition
{
    On, Off, Increase, Decrease, Open, Close, Lock, Unlock, Enter, Set, Reached,
}

/// <summary>
/// One atomic capability delta, resolved against the read-models (roadmap Epic 2N). This is the smallest
/// language-neutral unit of the story IR; a plain class (not a positional record) so it round-trips cleanly
/// through BSON when persisted inside <see cref="HomeStoryEntry.Scenes"/>.
/// </summary>
public class Beat
{
    public DateTime Timestamp { get; set; }

    /// <summary>Resolved persona (from TriggerSource / AdapterSource).</summary>
    public PersonaRole Actor { get; set; }

    /// <summary>Effective device archetype (2D), e.g. <c>light</c>, <c>valve</c>, <c>thermostat</c>, <c>sun</c>.</summary>
    public string ArchetypeKey { get; set; } = string.Empty;

    /// <summary>Capability that changed (<c>on_off</c>, <c>brightness</c>, <c>temperature</c>, <c>is_dark</c>).</summary>
    public string CapabilityId { get; set; } = string.Empty;

    public Transition Transition { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>Stable ref into the device synonym pool (per-device override id or the archetype key).</summary>
    public string DeviceRef { get; set; } = string.Empty;

    public string ZoneId { get; set; } = string.Empty;

    /// <summary>Zone display name (e.g. «Гостиная») — matched against the pack's common-room-name forms.</summary>
    public string? ZoneName { get; set; }

    /// <summary>Zone kind (<c>room</c>/<c>outdoor</c>/<c>gate</c>…) — drives preposition/case fallback.</summary>
    public string? ZoneKind { get; set; }

    public object? OldValue { get; set; }

    public object? NewValue { get; set; }

    /// <summary>Rule that caused it (Epic 1A), if any — links to the causal clause.</summary>
    public string? RuleId { get; set; }

    /// <summary>Lifted <c>auto_history.TriggerSummary</c> (Epic 1F) — the raw "why", turned into a subclause.</summary>
    public string? CauseSummary { get; set; }

    /// <summary>Home mode at the time of the event (Epic 1G).</summary>
    public string? Mode { get; set; }
}

/// <summary>
/// A causal cluster of <see cref="Beat"/>s sharing a causal root <c>(RuleId | mode-change, time-bucket)</c>
/// — rendered as a single sentence so a mode switch dragging N devices reads as one line, not N (Epic 2N).
/// </summary>
public class Scene
{
    public string Id { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime EndedAt { get; set; }

    /// <summary>Dominant actor of the cluster.</summary>
    public PersonaRole Actor { get; set; }

    /// <summary>Causal root key: <c>rule:{id}</c> | <c>mode:{old→new}</c> | <c>manual:{deviceId}</c>.</summary>
    public string CausalRootKey { get; set; } = string.Empty;

    /// <summary>Lifted causal phrase, if any (from a rule's trigger summary or a System-sensor transition).</summary>
    public string? Cause { get; set; }

    public string? Mode { get; set; }

    public List<Beat> Beats { get; set; } = new();

    /// <summary>Significance score (roadmap Epic 2N) — the diary's main filter.</summary>
    public double Significance { get; set; }

    /// <summary>Which significance signals fired (audit/debug + future LLM hints), e.g. <c>mode:Vacation</c>.</summary>
    public List<string> SignificanceReasons { get; set; } = new();
}

/// <summary>
/// One day of narration (roadmap Epic 2N) — the persisted unit. Holds the chosen, significance-ordered
/// scenes plus the aggregate day score used to decide whether the day is narrated at all («молчание — фича»).
/// </summary>
public class DayStory
{
    /// <summary>Local calendar day (stored UTC-truncated on <see cref="HomeStoryEntry"/>).</summary>
    public DateOnly Date { get; set; }

    public string TimeZoneId { get; set; } = string.Empty;

    public List<Scene> Scenes { get; set; } = new();

    public double DayScore { get; set; }
}
