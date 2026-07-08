// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Narrative;

/// <summary>
/// Persisted rotation cursor for the diary renderer (roadmap Epic 2N). Synonym choice uses a deterministic
/// <b>cooldown/LRU</b> — never randomness (the codebase forbids <c>Random</c>/<c>Date.now</c> in these paths,
/// so replay is exact). We keep the last-used index per pool and the day-sequence when it was last used, so
/// the same history + pack + state always renders identical prose, and a restart never resets the rotation
/// (which would cause "thesaurus disease"). Single document, id <c>current</c>, in <c>narrative_state</c>.
/// </summary>
public class NarrativeState
{
    public string Id { get; set; } = "current";

    /// <summary>Pool key → index last chosen from that pool.</summary>
    public Dictionary<string, int> LastUsedIndex { get; set; } = new();

    /// <summary>Pool key → the <see cref="EntryCounter"/> value when it was last used (drives cooldown).</summary>
    public Dictionary<string, int> LastUsedEntryNo { get; set; } = new();

    /// <summary>Monotonic counter of rendered day-entries — the cooldown clock.</summary>
    public int EntryCounter { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
