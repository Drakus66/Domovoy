// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Scenes;

/// <summary>
/// A first-class scene (roadmap Epic 3B): a named snapshot of target capability states for a set of
/// devices. Activating a scene replays each target as a <c>DeviceCommandV1</c> down the existing
/// actuation path (Epic 1D) — a scene only <i>groups</i> commands, it is not a new control mechanism.
/// A scene is a distinct entity from an automation rule; a rule can activate one via the
/// <c>ActionType.Scene</c> action, a dashboard tile can activate one directly, and the assistant can
/// author one. Editing a scene never touches the house — only an explicit activation publishes commands.
/// Shared by the DbGateway (persistence), ApiGateway (activation), AutomationService (the scene action)
/// and WebUI, so — like <see cref="Automations.AutomationRule"/> — it is one plain mutable shape that
/// serializes cleanly to both JSON (wire/UI) and BSON (Mongo).
/// </summary>
public class Scene
{
    /// <summary>Stable scene id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Optional icon key for the WebUI (scene list / dashboard tile). Presentation only.</summary>
    public string? Icon { get; set; }

    /// <summary>The per-device target states this scene applies, in order.</summary>
    public List<SceneTarget> Targets { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One device's target state within a <see cref="Scene"/>: the capability set to apply, mirroring the
/// shape of an <c>ActionType.Command</c> action (device id + capability set). Only writable capabilities
/// are captured; values are plain BCL primitives once persisted (the gateway normalizes them at the HTTP
/// boundary), so replaying them as a command is a clean round-trip.
/// </summary>
public class SceneTarget
{
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Capability id → target value, e.g. <c>{ "on_off": true, "brightness": 40 }</c>.</summary>
    public Dictionary<string, object?> Set { get; set; } = new();
}
