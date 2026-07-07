using Domovoy.CommutePlugin;
using Domovoy.CommutePlugin.Model;
using Domovoy.CommutePlugin.Traffic;

using Xunit;

namespace Domovoy.CommutePlugin.Tests;

public class GeoPointTests
{
    [Theory]
    [InlineData("55.7558, 37.6173", 55.7558, 37.6173)]
    [InlineData("55.7558,37.6173", 55.7558, 37.6173)]
    [InlineData("-33.87,151.21", -33.87, 151.21)]
    public void TryParseLatLon_parses_valid_pairs(string text, double lat, double lon)
    {
        Assert.True(GeoPoint.TryParseLatLon(text, out var p));
        Assert.Equal(lat, p.Latitude, 4);
        Assert.Equal(lon, p.Longitude, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Red Square, Moscow")]
    [InlineData("55.7558")]
    [InlineData("91,0")]      // latitude out of range
    [InlineData("0,181")]     // longitude out of range
    public void TryParseLatLon_rejects_non_coordinates(string text)
    {
        Assert.False(GeoPoint.TryParseLatLon(text, out _));
    }

    [Fact]
    public void DistanceKmTo_matches_known_great_circle()
    {
        var moscow = new GeoPoint(55.7558, 37.6173);
        var stPetersburg = new GeoPoint(59.9343, 30.3351);
        // Great-circle Moscow↔St Petersburg is ~633 km.
        Assert.Equal(633, moscow.DistanceKmTo(stPetersburg), 0);
    }
}

public class SimulatedTrafficProviderTests
{
    private static readonly GeoPoint Home = new(55.75, 37.61);
    private static readonly GeoPoint Work = new(55.80, 37.70);

    [Theory]
    [InlineData(8, 1.7)]    // morning rush
    [InlineData(18, 1.8)]   // evening rush
    [InlineData(3, 0.85)]   // night
    [InlineData(13, 1.0)]   // midday
    public void CongestionFactor_follows_time_of_day(int hour, double expected)
    {
        Assert.Equal(expected, SimulatedTrafficProvider.CongestionFactor(hour), 2);
    }

    [Theory]
    [InlineData(1.8, CommuteCapabilities.Traffic.Heavy)]
    [InlineData(1.3, CommuteCapabilities.Traffic.Moderate)]
    [InlineData(0.85, CommuteCapabilities.Traffic.Light)]
    public void LevelFor_buckets_congestion(double congestion, string expected)
    {
        Assert.Equal(expected, SimulatedTrafficProvider.LevelFor(congestion));
    }

    [Fact]
    public async Task Estimate_is_deterministic_for_same_inputs()
    {
        var provider = new SimulatedTrafficProvider(avgSpeedKmh: 45, fixedOverheadMinutes: 3);
        var at = new DateTimeOffset(2026, 7, 6, 13, 0, 0, TimeSpan.Zero);

        var a = await provider.EstimateAsync(Home, Work, at, null, default);
        var b = await provider.EstimateAsync(Home, Work, at, null, default);

        Assert.NotNull(a);
        Assert.Equal(a!.TravelMinutes, b!.TravelMinutes);
        Assert.Equal(a.DistanceKm, b.DistanceKm);
        Assert.True(a.DistanceKm > 0);
    }

    [Fact]
    public async Task Rush_hour_takes_longer_than_night()
    {
        var provider = new SimulatedTrafficProvider(avgSpeedKmh: 45, fixedOverheadMinutes: 3);
        var rush = new DateTimeOffset(2026, 7, 6, 18, 0, 0, TimeSpan.Zero);
        var night = new DateTimeOffset(2026, 7, 6, 3, 0, 0, TimeSpan.Zero);

        var rushEstimate = await provider.EstimateAsync(Home, Work, rush, null, default);
        var nightEstimate = await provider.EstimateAsync(Home, Work, night, null, default);

        Assert.True(rushEstimate!.TravelMinutes > nightEstimate!.TravelMinutes);
        Assert.Equal(CommuteCapabilities.Traffic.Heavy, rushEstimate.TrafficLevel);
        Assert.True(rushEstimate.TrafficDelayMinutes > 0);
        Assert.Equal(0, nightEstimate.TrafficDelayMinutes); // no delay below free-flow
    }

    [Fact]
    public async Task Deadline_uses_arrival_hour_for_congestion()
    {
        var provider = new SimulatedTrafficProvider(avgSpeedKmh: 45, fixedOverheadMinutes: 3);
        var nowNight = new DateTimeOffset(2026, 7, 6, 3, 0, 0, TimeSpan.Zero);
        var arriveRush = new DateTimeOffset(2026, 7, 6, 18, 0, 0, TimeSpan.Zero);

        // Departing at 03:00 but arriving in the 18:00 rush → the rush congestion applies.
        var estimate = await provider.EstimateAsync(Home, Work, nowNight, arriveRush, default);

        Assert.Equal(CommuteCapabilities.Traffic.Heavy, estimate!.TrafficLevel);
    }
}

public class CommuteMathTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 6, 8, 0, 0, TimeSpan.FromHours(3)); // 08:00 local

    [Fact]
    public void ParseDeadline_keeps_a_future_time_today()
    {
        var deadline = CommuteMath.ParseDeadline("09:30", Now);
        Assert.NotNull(deadline);
        Assert.Equal(6, deadline!.Value.Day);
        Assert.Equal(9, deadline.Value.Hour);
        Assert.Equal(30, deadline.Value.Minute);
    }

    [Fact]
    public void ParseDeadline_rolls_a_passed_time_to_tomorrow()
    {
        var deadline = CommuteMath.ParseDeadline("07:00", Now); // already past 08:00
        Assert.NotNull(deadline);
        Assert.Equal(7, deadline!.Value.Day);
        Assert.Equal(7, deadline.Value.Hour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-time")]
    public void ParseDeadline_returns_null_for_bad_input(string? input)
    {
        Assert.Null(CommuteMath.ParseDeadline(input, Now));
    }

    [Theory]
    [InlineData(120, 300)]
    [InlineData(45, 120)]
    [InlineData(20, 60)]
    [InlineData(5, 30)]
    public void AdaptiveInterval_tightens_as_key_moment_approaches(double keyMinutes, int expected)
    {
        Assert.Equal(expected, CommuteMath.AdaptiveIntervalSeconds(keyMinutes, minSeconds: 30));
    }
}
