namespace Domovoy.CommutePlugin.Model;

/// <summary>
/// The answer from a traffic provider for one leg: how far, how long with current/predicted traffic, and
/// a coarse congestion bucket. Provider-agnostic so the simulated and TomTom backends are interchangeable.
/// </summary>
/// <param name="DistanceKm">Road distance, km.</param>
/// <param name="TravelMinutes">Traffic-aware travel time, minutes.</param>
/// <param name="TrafficDelayMinutes">Portion of <paramref name="TravelMinutes"/> attributable to congestion.</param>
/// <param name="TrafficLevel">One of <see cref="CommuteCapabilities.Traffic"/>.</param>
public sealed record RouteEstimate(
    double DistanceKm,
    double TravelMinutes,
    double TrafficDelayMinutes,
    string TrafficLevel);
