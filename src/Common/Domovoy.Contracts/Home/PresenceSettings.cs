// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// Presence-layer configuration (roadmap Epic 3D). A single persisted document the DbGateway owns and the
/// AutomationService's presence layer reads. It holds the server-side geofence (radius around the site
/// location, 2K) and the anti-flap grace, plus an optional shared token gating the OwnTracks ingest
/// endpoint. Sensible defaults so a fresh install already geofences correctly once residents are added.
/// </summary>
public class PresenceSettings
{
    /// <summary>There is only ever one presence-settings document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// Radius (metres) around the site location (2K) counted as "home". A reported position within this
    /// distance ⇒ present; outside ⇒ away. Sized to cover a plot + GPS jitter, not a single point.
    /// </summary>
    public double HomeRadiusMeters { get; set; } = 150;

    /// <summary>
    /// Hysteresis: a resident already home must stay outside the geofence this long before flipping to away.
    /// Absorbs GPS jitter and a brief step outside so the home doesn't bounce to Away and back. Arrival is
    /// immediate (crossing in is trusted); only departure waits out the grace.
    /// </summary>
    public int AwayGraceSeconds { get; set; } = 180;

    /// <summary>
    /// Optional shared secret the OwnTracks ingest endpoint requires as <c>?key=</c> (or a matching
    /// <c>X-Presence-Token</c> header). Null/blank ⇒ the endpoint is open (dev / trusted LAN); set it when
    /// the endpoint is reachable off-LAN so a stranger can't spoof presence. Never returned to clients.
    /// </summary>
    public string? OwnTracksToken { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
