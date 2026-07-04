namespace Domovoy.AutomationService.Services;

/// <summary>
/// Computes sunrise/sunset (UTC) for the configured site using the standard sunrise equation — no
/// external dependency, so it works fully offline (roadmap principle 2). Accurate to ~1 minute, which
/// is plenty for outdoor-lighting triggers. Polar day/night falls back to "not dark".
/// </summary>
public sealed class SunCalculator
{
    private readonly double _lat;
    private readonly double _lon;
    private const double Rad = Math.PI / 180.0;

    public SunCalculator(double latitude, double longitude)
    {
        _lat = latitude;
        _lon = longitude;
    }

    /// <summary>Sunrise/sunset in UTC for the given day; (null, null) during polar day/night.</summary>
    public (DateTime? Sunrise, DateTime? Sunset) ForDate(DateTime dateUtc)
    {
        var midnight = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var jDate = ToJulian(midnight);
        var n = Math.Ceiling(jDate - 2451545.0 + 0.0008);
        var lw = -_lon;
        var jStar = n - lw / 360.0;
        var m = Mod360(357.5291 + 0.98560028 * jStar);
        var c = 1.9148 * Math.Sin(m * Rad) + 0.0200 * Math.Sin(2 * m * Rad) + 0.0003 * Math.Sin(3 * m * Rad);
        var lambda = Mod360(m + c + 180 + 102.9372);
        var jTransit = 2451545.0 + jStar + 0.0053 * Math.Sin(m * Rad) - 0.0069 * Math.Sin(2 * lambda * Rad);
        var delta = Math.Asin(Math.Sin(lambda * Rad) * Math.Sin(23.4397 * Rad)) / Rad;
        var cosOmega = (Math.Sin(-0.833 * Rad) - Math.Sin(_lat * Rad) * Math.Sin(delta * Rad))
                       / (Math.Cos(_lat * Rad) * Math.Cos(delta * Rad));

        if (cosOmega is > 1 or < -1) return (null, null); // polar night / day

        var omega = Math.Acos(cosOmega) / Rad;
        return (FromJulian(jTransit - omega / 360.0), FromJulian(jTransit + omega / 360.0));
    }

    /// <summary>True if the given instant is before sunrise or after sunset (i.e. dark).</summary>
    public bool IsDark(DateTimeOffset nowUtc)
    {
        var (sunrise, sunset) = ForDate(nowUtc.UtcDateTime);
        if (sunrise is null || sunset is null) return false; // polar fallback
        var t = nowUtc.UtcDateTime;
        return t < sunrise.Value || t > sunset.Value;
    }

    private static double ToJulian(DateTime utc) => utc.ToOADate() + 2415018.5;
    private static DateTime FromJulian(double j) =>
        DateTime.SpecifyKind(DateTime.FromOADate(j - 2415018.5), DateTimeKind.Utc);
    private static double Mod360(double x) { x %= 360; return x < 0 ? x + 360 : x; }
}
