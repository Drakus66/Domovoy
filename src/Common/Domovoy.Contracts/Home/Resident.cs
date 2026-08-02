// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// A tracked household member (roadmap Epic 3D — presence as a platform signal). A resident is the
/// <b>identity</b> whose home/away state the platform tracks and projects as a virtual <c>person</c> device
/// (capability <c>presence</c>); the aggregate over all residents feeds the <c>presence_mode</c> block (1G),
/// rules and scenes. It is deliberately a thin record, linkable to a <see cref="Security.User"/> (2E) but
/// independent of it — a home can track a guest phone that has no login. Persisted in the
/// <c>residents</c> collection (DbGateway owns it).
///
/// <para>Presence itself is <b>not</b> stored here: it is live signal state held by the AutomationService's
/// presence layer and published on the person device, exactly like the Home/Power virtual devices' live
/// values (see SystemSensorService). This document is only the durable roster + how a source maps to it.</para>
/// </summary>
public class Resident
{
    /// <summary>Stable resident id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name shown on the dashboard and the person device.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional link to a local user (2E); null for an identity-only resident (e.g. a guest).</summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The key a geofencing source (OwnTracks) reports under — matched case-insensitively against the
    /// payload's tracker id (<c>tid</c>), MQTT/HTTP topic, or an explicit <c>?user=</c> query. Null ⇒ the
    /// resident has no geofence source bound yet (their presence can still be set by a rule/other source).
    /// </summary>
    public string? OwnTracksId { get; set; }

    /// <summary>Whether this resident participates in presence tracking + the home/away aggregate.</summary>
    public bool TrackingEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
