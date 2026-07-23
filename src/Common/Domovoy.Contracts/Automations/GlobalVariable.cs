// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Automations;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VariableType { Number, Boolean, String, DateTime, List }

/// <summary>
/// A named, persistent global variable (roadmap Epic 3E, Hubitat "Hub Variables"). Config (name/type/
/// description) and the live value both live on this one document in the <c>variables</c> collection —
/// unlike control-block config/state (Epic 2Q), a variable has no periodic tick to dedupe writes against,
/// so there is no separate "variable_state" collection.
///
/// <para>A variable is also projected as its own virtual capability device (<c>VariableRuntimeService</c>,
/// capability <c>CapabilityIds.VariableValue</c>) — this is the "connector-device" requirement from the
/// roadmap: rules read/write it exactly like any other device capability (triggers, conditions, the
/// existing <see cref="ActionType.Command"/> action, control-block port bindings), with no separate
/// variable-reference syntax to add anywhere else.</para>
/// </summary>
public class GlobalVariable
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public VariableType Type { get; set; } = VariableType.Number;

    public string? Description { get; set; }

    /// <summary>
    /// Current value: <c>double</c> for <see cref="VariableType.Number"/>, <c>bool</c> for
    /// <see cref="VariableType.Boolean"/>, a plain string for <see cref="VariableType.String"/>, an ISO-8601
    /// string for <see cref="VariableType.DateTime"/>, and a JSON-encoded string for
    /// <see cref="VariableType.List"/> (opaque structured value — not itself a capability array; the
    /// roadmap decision against arrays-in-capability, Epic 3C decision #9d, is about telemetry series, not
    /// user-authored variable payloads).
    /// </summary>
    public object? Value { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
