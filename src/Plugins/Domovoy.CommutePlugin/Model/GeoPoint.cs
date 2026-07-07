// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;

namespace Domovoy.CommutePlugin.Model;

/// <summary>A WGS84 coordinate with an optional display label.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude, string? Label = null)
{
    /// <summary>Great-circle distance to <paramref name="other"/> in kilometres (haversine).</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        const double earthRadiusKm = 6371.0088;
        double dLat = ToRad(other.Latitude - Latitude);
        double dLon = ToRad(other.Longitude - Longitude);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(ToRad(Latitude)) * Math.Cos(ToRad(other.Latitude))
                   * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    /// <summary>
    /// Parses a <c>"lat,lon"</c> pair, optionally carrying a display label as <c>"lat,lon|Label"</c> (the form
    /// the map picker writes). False for place names or malformed input.
    /// </summary>
    public static bool TryParseLatLon(string? text, out GeoPoint point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Strip an optional "|Label" suffix — the coordinates are before the pipe.
        var pipe = text.IndexOf('|');
        var label = pipe >= 0 ? text[(pipe + 1)..].Trim() : null;
        var coords = pipe >= 0 ? text[..pipe] : text;

        var parts = coords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;

        if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
            && lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
        {
            point = new GeoPoint(lat, lon, string.IsNullOrWhiteSpace(label) ? null : label);
            return true;
        }
        return false;
    }
}
