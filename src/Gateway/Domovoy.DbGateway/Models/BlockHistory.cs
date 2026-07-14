// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// A single control-block run record (roadmap Epic 1H). Persisted by the EventInterceptor from
/// <c>BlockTriggeredV1</c> into the <c>block_history</c> collection. A block is an active entity that
/// ticks continuously, so — unlike a rule's <see cref="AutoHistory"/> — a record is written only when the
/// block's emitted decision changes or a tick fails (the runtime dedupes on the emitted signature). This
/// backs the <c>block</c> Activity source, giving the block layer first-class visibility in the journal,
/// including a Shadow-stage ML governor that proposes a value but drives nothing.
/// </summary>
public class BlockHistory
{
    /// <summary>Collection this record lives in.</summary>
    public const string Collection = "block_history";

    [BsonId]
    public ObjectId Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string BlockId { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;

    /// <summary>Block type id (e.g. <c>ml_thermostat</c>, <c>hysteresis</c>).</summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>Whether the tick completed without throwing.</summary>
    public bool Ok { get; set; }

    /// <summary>Human-readable one-liner of what the block emitted (or "tick failed").</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Error message on a failing tick, else null.</summary>
    public string? Detail { get; set; }
}
