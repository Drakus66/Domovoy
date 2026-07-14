// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;
using System.Text.Json;

using Domovoy.CommutePlugin.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.CommutePlugin.Services;

/// <summary>
/// Reads the installation's own settings back through the public API gateway — treating the core as a black
/// box (roadmap Epic 2M). The home <b>origin</b> is the runtime-editable site location (Epic 2K), so moving
/// the house on the Settings map is picked up here with no plugin change; the same gateway's optional
/// geocoder (Epic 2K) turns a destination place name into coordinates. Everything degrades to null on
/// failure so the planner can carry on with a "lat,lon" destination and its last-known home.
/// </summary>
public sealed class SettingsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<SettingsClient> _logger;

    public SettingsClient(HttpClient http, ILogger<SettingsClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Current site location (the commute origin) or null when the gateway is unreachable.</summary>
    public async Task<SiteLocationDto?> GetHomeAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<SiteLocationDto>("api/settings/location", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read site location from the gateway");
            return null;
        }
    }

    /// <summary>Forward-geocode a place name → first candidate, or null (offline / no match / bad input).</summary>
    public async Task<GeoPoint?> GeocodeAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            var url = $"api/settings/geocode?q={Uri.EscapeDataString(query.Trim())}";
            var hits = await _http.GetFromJsonAsync<List<GeocodeCandidate>>(url, Json, ct);
            var first = hits?.FirstOrDefault();
            return first is null ? null : new GeoPoint(first.Latitude, first.Longitude, first.Label);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Geocode of '{Query}' unavailable (offline?)", query);
            return null;
        }
    }

    /// <summary>Site location document as served by <c>/api/settings/location</c> (Epic 2K).</summary>
    public sealed record SiteLocationDto(
        double Latitude, double Longitude, string? Label, string? TimeZoneId);

    private sealed record GeocodeCandidate(string Label, double Latitude, double Longitude);
}
