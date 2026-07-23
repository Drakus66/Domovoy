// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// One node of the home's <b>electrical</b> topology (roadmap Epic 3C-D): a supply, a panel or a circuit
/// breaker. Nodes form a tree through <see cref="ParentId"/> — supply → panel → circuit — and devices hang off
/// a circuit via <c>EnergyProfile.CircuitId</c>, inheriting its phase. This is orthogonal to <see cref="Zone"/>:
/// one circuit commonly feeds several rooms, and one room is commonly fed by several circuits.
/// <para>What it buys: consumption and live load broken down per phase and per circuit, a balance check
/// against a node's own meter (what the meter sees minus what the known devices explain = unaccounted), and
/// per-phase / per-breaker budgets for the load coordinator.</para>
/// </summary>
public class PowerNode
{
    /// <summary>Stable node id (GUID as string). Generated on create when not supplied.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Node kind: <c>supply</c> (grid intake / generator / inverter / solar), <c>panel</c>
    /// (distribution board) or <c>circuit</c> (a breaker devices hang off). See <see cref="PowerNodeKinds"/>.</summary>
    public string Kind { get; set; } = PowerNodeKinds.Circuit;

    /// <summary>Parent node; null for a supply (the root of a tree).</summary>
    public string? ParentId { get; set; }

    /// <summary>Phase this node carries: <c>l1</c>/<c>l2</c>/<c>l3</c>, or <c>three</c> for a three-phase node
    /// (its load is split evenly across the phases). Null ⇒ inherited from the parent.</summary>
    public string? Phase { get; set; }

    /// <summary>Breaker rating (A) — the default limit for a per-circuit budget. Null ⇒ unrated.</summary>
    public double? BreakerAmps { get; set; }

    /// <summary>Nominal voltage (V) used to turn <see cref="BreakerAmps"/> into watts; null ⇒ the site default.</summary>
    public double? Voltage { get; set; }

    /// <summary>Device (with <c>power</c>/<c>energy</c>) metering this node — the reference for the balance
    /// check. Null ⇒ the node's load is only the sum of what hangs off it.</summary>
    public string? MeterDeviceId { get; set; }

    /// <summary>For a <c>supply</c>: which <c>power_source</c> tier it represents (grid/battery/solar…), tying
    /// the topology to the load-management signal (Epic 3C-LM). Null for panels/circuits.</summary>
    public string? PowerSourceKind { get; set; }

    /// <summary>Explicit ordering among siblings; lower comes first.</summary>
    public int Order { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Node kinds of the electrical tree — a closed set, unlike the open zone <c>Kind</c>: the balance
/// and budget maths depend on knowing which level a node is.</summary>
public static class PowerNodeKinds
{
    public const string Supply = "supply";
    public const string Panel = "panel";
    public const string Circuit = "circuit";

    public static readonly IReadOnlyList<string> All = new[] { Supply, Panel, Circuit };

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

/// <summary>Phases a node can carry. <c>three</c> marks a three-phase node whose load spreads evenly.</summary>
public static class PowerPhases
{
    public const string L1 = "l1";
    public const string L2 = "l2";
    public const string L3 = "l3";
    public const string Three = "three";

    /// <summary>The single phases, in display order — what a per-phase breakdown reports.</summary>
    public static readonly IReadOnlyList<string> Single = new[] { L1, L2, L3 };

    public static readonly IReadOnlyList<string> All = new[] { L1, L2, L3, Three };

    public static bool IsKnown(string? phase) => phase is not null && All.Contains(phase);
}
