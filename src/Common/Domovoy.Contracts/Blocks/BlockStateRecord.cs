// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Blocks;

/// <summary>
/// Persisted runtime state of one control block (roadmap Epic 2Q, Phase 2) in the <c>block_state</c>
/// collection. A block's private state bag (a latch's held value, a counter's count, a timer's start instant)
/// is serialized to an opaque JSON string so heterogeneous values survive a restart without BSON typing
/// concerns. Loaded once on startup and re-seeded into the running instance; snapshotted periodically. Stored
/// separately from the block's config (<see cref="ControlBlock"/>) so a config edit never clobbers live state.
/// </summary>
public class BlockStateRecord
{
    /// <summary>The block id this state belongs to (matches <see cref="ControlBlock.Id"/>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The block's state bag serialized as JSON (keys → bool/number/string/array/object values).</summary>
    public string StateJson { get; set; } = "{}";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
