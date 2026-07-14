// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.CommutePlugin.Model;

namespace Domovoy.CommutePlugin.Traffic;

/// <summary>
/// Deterministic, fully-offline traffic estimate: haversine distance at a configured average speed, scaled
/// by a time-of-day congestion curve (rush hours slow, night fast). No key, no network — it lets the plugin
/// (and the whole PluginSupervisor path, Epic 1C) run and be exercised end-to-end without a cloud account,
/// and gives a realistic-looking ETA that varies through the day. Congestion is keyed off the relevant hour
/// (the arrival hour in deadline mode, otherwise the departure hour), so the result is a pure function of
/// its inputs and is unit-testable.
/// </summary>
public sealed class SimulatedTrafficProvider : ITrafficProvider
{
    private readonly double _avgSpeedKmh;
    private readonly double _fixedOverheadMinutes;

    public SimulatedTrafficProvider(double avgSpeedKmh, double fixedOverheadMinutes)
    {
        _avgSpeedKmh = avgSpeedKmh <= 0 ? 45 : avgSpeedKmh;
        _fixedOverheadMinutes = Math.Max(0, fixedOverheadMinutes);
    }

    public string Name => "simulated";

    public Task<RouteEstimate?> EstimateAsync(
        GeoPoint from, GeoPoint to, DateTimeOffset now, DateTimeOffset? arriveBy, CancellationToken ct)
    {
        var distanceKm = from.DistanceKmTo(to);
        // Roads are never straight lines — inflate the great-circle distance to an approximate driving distance.
        var roadKm = distanceKm * 1.35;

        var referenceHour = (arriveBy ?? now).Hour;
        var congestion = CongestionFactor(referenceHour);

        var freeFlowMinutes = roadKm / _avgSpeedKmh * 60.0;
        var travelMinutes = freeFlowMinutes * congestion + _fixedOverheadMinutes;
        var delayMinutes = congestion > 1 ? freeFlowMinutes * (congestion - 1) : 0;

        var estimate = new RouteEstimate(
            DistanceKm: Math.Round(roadKm, 1),
            TravelMinutes: Math.Round(travelMinutes, 1),
            TrafficDelayMinutes: Math.Round(delayMinutes, 1),
            TrafficLevel: LevelFor(congestion));

        return Task.FromResult<RouteEstimate?>(estimate);
    }

    /// <summary>A simple bimodal weekday congestion curve; &gt;1 means slower than free-flow.</summary>
    internal static double CongestionFactor(int hour) => hour switch
    {
        7 or 8 or 9 => 1.7,          // morning rush
        17 or 18 or 19 => 1.8,       // evening rush (worse)
        6 or 10 or 16 or 20 => 1.25, // shoulders
        22 or 23 or (>= 0 and <= 5) => 0.85, // free-flowing night
        _ => 1.0,
    };

    internal static string LevelFor(double congestion) => congestion switch
    {
        >= 1.6 => CommuteCapabilities.Traffic.Heavy,
        >= 1.2 => CommuteCapabilities.Traffic.Moderate,
        _ => CommuteCapabilities.Traffic.Light,
    };
}
