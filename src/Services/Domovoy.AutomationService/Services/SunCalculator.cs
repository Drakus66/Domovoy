// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Computes sunrise/sunset (UTC) for the configured site using the standard sunrise equation — no
/// external dependency, so it works fully offline (roadmap principle 2). Accurate to ~1 minute, which
/// is plenty for outdoor-lighting triggers. Polar day/night falls back to "not dark".
/// </summary>
public sealed class SunCalculator
{
    private sealed record Coord(double Lat, double Lon);

    // The site is runtime-editable (Epic 2K): RefreshLoop repoints the calculator when the WebUI location
    // changes. Holding lat/lon as one immutable record behind a volatile reference means a concurrent tick
    // reads a consistent pair (no torn lat/lon) and always sees the latest update.
    private volatile Coord _coord;
    private const double Rad = Math.PI / 180.0;

    public SunCalculator(double latitude, double longitude)
        => _coord = new Coord(latitude, longitude);

    public double Latitude => _coord.Lat;
    public double Longitude => _coord.Lon;

    /// <summary>
    /// Repoint the calculator at a new site (roadmap Epic 2K) — the location is now runtime-editable from
    /// the WebUI Settings page and refreshed from the DbGateway, so it no longer needs a redeploy.
    /// </summary>
    public void Update(double latitude, double longitude) => _coord = new Coord(latitude, longitude);

    /// <summary>Sunrise/sunset in UTC for the given day; (null, null) during polar day/night.</summary>
    public (DateTime? Sunrise, DateTime? Sunset) ForDate(DateTime dateUtc)
    {
        var coord = _coord; // snapshot once so a concurrent Update can't tear this computation
        var midnight = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var jDate = ToJulian(midnight);
        var n = Math.Ceiling(jDate - 2451545.0 + 0.0008);
        // Longitude correction to mean solar time. This term must be POSITIVE for an eastern longitude so
        // solar transit lands earlier in UTC (sun crosses the meridian ~4 min per degree east before Greenwich).
        // A flipped sign here pushes transit — and thus sunrise/sunset — the wrong way by 2×longitude/15 hours
        // (e.g. Moscow's ~03:50 sunrise became a nonsensical value inconsistent with the timezone).
        var lw = coord.Lon;
        var jStar = n - lw / 360.0;
        var m = Mod360(357.5291 + 0.98560028 * jStar);
        var c = 1.9148 * Math.Sin(m * Rad) + 0.0200 * Math.Sin(2 * m * Rad) + 0.0003 * Math.Sin(3 * m * Rad);
        var lambda = Mod360(m + c + 180 + 102.9372);
        var jTransit = 2451545.0 + jStar + 0.0053 * Math.Sin(m * Rad) - 0.0069 * Math.Sin(2 * lambda * Rad);
        var delta = Math.Asin(Math.Sin(lambda * Rad) * Math.Sin(23.4397 * Rad)) / Rad;
        var cosOmega = (Math.Sin(-0.833 * Rad) - Math.Sin(coord.Lat * Rad) * Math.Sin(delta * Rad))
                       / (Math.Cos(coord.Lat * Rad) * Math.Cos(delta * Rad));

        if (cosOmega is > 1 or < -1) return (null, null); // polar night / day

        var omega = Math.Acos(cosOmega) / Rad;
        return (FromJulian(jTransit - omega / 360.0), FromJulian(jTransit + omega / 360.0));
    }

    /// <summary>
    /// True if the given instant is before sunrise or after sunset (i.e. dark). A positive
    /// <paramref name="offsetMinutes"/> widens the dark window on both ends (lights come on before
    /// sunset and stay on after sunrise) — the hook for outdoor lighting that leads dusk.
    /// </summary>
    public bool IsDark(DateTimeOffset nowUtc, double offsetMinutes = 0)
    {
        var (sunrise, sunset) = ForDate(nowUtc.UtcDateTime);
        if (sunrise is null || sunset is null) return false; // polar fallback
        var t = nowUtc.UtcDateTime;
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        return t < sunrise.Value + offset || t > sunset.Value - offset;
    }

    /// <summary>
    /// Solar position at an instant (roadmap Epic 2L) — elevation above the horizon and azimuth clockwise
    /// from north, both in degrees. NOAA solar-position algorithm; no external dependency, works offline.
    /// Elevation is geometric (no atmospheric-refraction correction), which is plenty for automation. Powers
    /// the system Sun sensor (<c>sun_elevation</c>/<c>sun_azimuth</c>).
    /// </summary>
    public (double ElevationDeg, double AzimuthDeg) Position(DateTimeOffset instant)
    {
        var coord = _coord;
        var utc = instant.UtcDateTime;
        var jc = (ToJulian(utc) - 2451545.0) / 36525.0; // Julian century since J2000.0

        var gmls = Mod360(280.46646 + jc * (36000.76983 + jc * 0.0003032));      // geom mean longitude (°)
        var gmas = 357.52911 + jc * (35999.05029 - 0.0001537 * jc);              // geom mean anomaly (°)
        var eeo = 0.016708634 - jc * (0.000042037 + 0.0000001267 * jc);          // orbit eccentricity
        var seoc = Math.Sin(gmas * Rad) * (1.914602 - jc * (0.004817 + 0.000014 * jc))
                   + Math.Sin(2 * gmas * Rad) * (0.019993 - 0.000101 * jc)
                   + Math.Sin(3 * gmas * Rad) * 0.000289;                          // equation of centre
        var trueLong = gmls + seoc;
        var appLong = trueLong - 0.00569 - 0.00478 * Math.Sin((125.04 - 1934.136 * jc) * Rad);
        var moe = 23 + (26 + (21.448 - jc * (46.815 + jc * (0.00059 - jc * 0.001813))) / 60) / 60; // mean obliquity
        var oc = moe + 0.00256 * Math.Cos((125.04 - 1934.136 * jc) * Rad);        // obliquity correction (°)
        var decl = Math.Asin(Math.Sin(oc * Rad) * Math.Sin(appLong * Rad)) / Rad; // declination (°)

        var vary = Math.Tan(oc / 2 * Rad) * Math.Tan(oc / 2 * Rad);
        var eqTime = 4 * (vary * Math.Sin(2 * gmls * Rad)
                          - 2 * eeo * Math.Sin(gmas * Rad)
                          + 4 * eeo * vary * Math.Sin(gmas * Rad) * Math.Cos(2 * gmls * Rad)
                          - 0.5 * vary * vary * Math.Sin(4 * gmls * Rad)
                          - 1.25 * eeo * eeo * Math.Sin(2 * gmas * Rad)) / Rad;    // equation of time (min)

        // True solar time (min). We work in UTC, so timezone is 0 and longitude adds 4 min per degree east.
        var minutesUtc = utc.TimeOfDay.TotalMinutes;
        var tst = Mod(minutesUtc + eqTime + 4 * coord.Lon, 1440);
        var ha = tst / 4 < 0 ? tst / 4 + 180 : tst / 4 - 180;                     // hour angle (°)

        var lat = coord.Lat;
        var zenith = Math.Acos(Math.Clamp(
            Math.Sin(lat * Rad) * Math.Sin(decl * Rad)
            + Math.Cos(lat * Rad) * Math.Cos(decl * Rad) * Math.Cos(ha * Rad), -1, 1)) / Rad;
        var elevation = 90 - zenith;

        double azimuth;
        var azDenom = Math.Cos(lat * Rad) * Math.Sin(zenith * Rad);
        if (Math.Abs(azDenom) > 1e-3)
        {
            var azRad = Math.Clamp(
                (Math.Sin(lat * Rad) * Math.Cos(zenith * Rad) - Math.Sin(decl * Rad)) / azDenom, -1, 1);
            var a = Math.Acos(azRad) / Rad;
            azimuth = ha > 0 ? Mod360(a + 180) : Mod360(540 - a);
        }
        else
        {
            azimuth = lat > 0 ? 180 : 0; // sun at the pole's zenith — azimuth is degenerate
        }

        return (elevation, azimuth);
    }

    private static double Mod(double x, double m) { x %= m; return x < 0 ? x + m : x; }

    private static double ToJulian(DateTime utc) => utc.ToOADate() + 2415018.5;
    private static DateTime FromJulian(double j) =>
        DateTime.SpecifyKind(DateTime.FromOADate(j - 2415018.5), DateTimeKind.Utc);
    private static double Mod360(double x) { x %= 360; return x < 0 ? x + 360 : x; }
}
