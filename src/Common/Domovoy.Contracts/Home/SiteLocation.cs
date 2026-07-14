// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// The installation's geographic location (roadmap Epic 2K). A single persisted document that the
/// DbGateway owns and the AutomationService reads for its sunrise/sunset geometry (the <c>sun_gate</c>
/// block and Sun triggers/conditions). Following the offline-first principle, the coordinates alone are
/// enough — sunrise/sunset is computed locally from them (<c>SunCalculator</c>), no external service is
/// ever required at runtime. A geocoder is used only, and optionally, to turn a place name or a map click
/// into coordinates while editing; the timezone is derived from the coordinates offline (GeoTimeZone) and
/// may be pinned by the user.
/// </summary>
public class SiteLocation
{
    /// <summary>There is only ever one location document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>Site latitude (WGS84). Defaults to Moscow until the user sets a location.</summary>
    public double Latitude { get; set; } = 55.7558;

    /// <summary>Site longitude (WGS84).</summary>
    public double Longitude { get; set; } = 37.6173;

    /// <summary>Human-readable place label (from the geocoder or hand-entered), for display only.</summary>
    public string? Label { get; set; }

    /// <summary>IANA timezone id (e.g. <c>Europe/Moscow</c>), derived from the coordinates or pinned by the user.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// When true, <see cref="TimeZoneId"/> is derived from the coordinates on every save (offline, via
    /// GeoTimeZone). When false, the user pinned the timezone and coordinate changes leave it untouched.
    /// </summary>
    public bool TimeZoneAuto { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
