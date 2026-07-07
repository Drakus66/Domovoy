using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;

namespace Domovoy.CommutePlugin;

/// <summary>
/// The plugin projects itself as three first-class devices (roadmap Epic 2M) — the general plugin shape of
/// <b>controllable inputs + sensor outputs</b>, all usable in automations:
/// <list type="bullet">
///   <item><b>Route · Start</b> — a controllable location (writable), defaulting to the site location.</item>
///   <item><b>Route · Destination</b> — a controllable location (writable) plus an optional arrive-by time.</item>
///   <item><b>Route · Forecast</b> — the read-only result (ETA, leave-by, traffic…).</item>
/// </list>
/// There is no explicit "mode": when the destination has a deadline the plugin computes the latest time to
/// leave; otherwise it gives the ETA departing now. Capability ids are plugin-scoped (<c>commute:*</c>).
/// </summary>
public static class CommuteCapabilities
{
    /// <summary>Adapter source that owns the devices (stable identity, drives the derived device ids).</summary>
    public const string Source = "Commute";

    // --- Writable inputs ---

    /// <summary>Start of the route as <c>"lat,lon"</c> / <c>"lat,lon|Label"</c>; empty = the site location.</summary>
    public const string Origin = "commute:origin";

    /// <summary>Destination of the route as <c>"lat,lon"</c> / <c>"lat,lon|Label"</c>.</summary>
    public const string Destination = "commute:destination";

    /// <summary>Optional arrive-by time (local <c>HH:mm</c>); empty = depart now.</summary>
    public const string Deadline = "commute:deadline";

    // --- Read-only outputs (the forecast) ---

    public const string EtaMinutes = "commute:eta_minutes";
    public const string DistanceKm = "commute:distance_km";
    public const string TrafficLevel = "commute:traffic_level";
    public const string LeaveBy = "commute:leave_by";
    public const string ArrivalEta = "commute:arrival_eta";
    public const string MinutesToArrival = "commute:minutes_to_arrival";
    public const string Status = "commute:status";

    /// <summary>Congestion buckets (enum values for <see cref="TrafficLevel"/>).</summary>
    public static class Traffic
    {
        public const string Unknown = "unknown";
        public const string Light = "light";
        public const string Moderate = "moderate";
        public const string Heavy = "heavy";

        public static readonly IReadOnlyList<string> All = new[] { Unknown, Light, Moderate, Heavy };
    }

    // --- Stable device ids (same every run) ---

    public static readonly Guid OriginDeviceId = DeviceIdFactory.Derive(Source, "origin");
    public static readonly Guid DestinationDeviceId = DeviceIdFactory.Derive(Source, "destination");
    public static readonly Guid ForecastDeviceId = DeviceIdFactory.Derive(Source, "forecast");

    public static Capability[] OriginCapabilities() => new[]
    {
        WellKnownCapabilities.Location(Origin, writable: true),
    };

    public static Capability[] DestinationCapabilities() => new[]
    {
        WellKnownCapabilities.Location(Destination, writable: true),
        WellKnownCapabilities.TimeInput(Deadline, writable: true),
    };

    public static Capability[] ForecastCapabilities() => new[]
    {
        WellKnownCapabilities.Number(EtaMinutes, "min", 0, null, step: 1, writable: false),
        WellKnownCapabilities.Number(DistanceKm, "km", 0, null, step: 0.1, writable: false),
        WellKnownCapabilities.Enum(TrafficLevel, Traffic.All, writable: false),
        WellKnownCapabilities.Text(LeaveBy, writable: false),
        WellKnownCapabilities.Text(ArrivalEta, writable: false),
        WellKnownCapabilities.Number(MinutesToArrival, "min", 0, null, step: 1, writable: false),
        WellKnownCapabilities.Text(Status, writable: false),
    };
}
