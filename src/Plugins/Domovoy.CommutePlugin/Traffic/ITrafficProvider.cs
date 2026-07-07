using Domovoy.CommutePlugin.Model;

namespace Domovoy.CommutePlugin.Traffic;

/// <summary>
/// A replaceable route/traffic backend (roadmap Epic 2M — "коннектор заменяемый"). The default is the
/// fully-offline <see cref="SimulatedTrafficProvider"/>; <see cref="TomTomTrafficProvider"/> plugs in when a
/// key is configured. Keeping this behind an interface means the RF-specific problem (Yandex traffic is the
/// best source there but its API is paid) is a one-class swap, not a rewrite.
/// </summary>
public interface ITrafficProvider
{
    /// <summary>Provider name for logs / diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Estimate the leg from <paramref name="from"/> to <paramref name="to"/>. <paramref name="arriveBy"/>,
    /// when set (deadline mode), asks the provider to predict traffic for that arrival time; otherwise the
    /// estimate is for departing at <paramref name="now"/>.
    /// </summary>
    Task<RouteEstimate?> EstimateAsync(
        GeoPoint from, GeoPoint to, DateTimeOffset now, DateTimeOffset? arriveBy, CancellationToken ct);
}
