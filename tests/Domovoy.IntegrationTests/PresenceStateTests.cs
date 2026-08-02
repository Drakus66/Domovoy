// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the presence decision logic (roadmap Epic 3D) — geofence, arrival/departure
/// hysteresis, roster resolution and the occupancy aggregate. Pure: <see cref="PresenceState"/> owns the
/// logic with no bus/DB, exactly so it can be exercised deterministically here.
/// </summary>
public sealed class PresenceStateTests
{
    // A home near central Moscow; the geofence radius below is 150 m.
    private const double HomeLat = 55.7558;
    private const double HomeLon = 37.6173;

    private static readonly IReadOnlyList<string> AnyaKey = new[] { "anya" };

    private static PresenceState WithAnya(double graceSeconds = 180)
    {
        var state = new PresenceState();
        state.Configure(radiusMeters: 150, awayGraceSeconds: graceSeconds, HomeLat, HomeLon);
        state.SyncRoster(new[]
        {
            new PresenceState.ResidentEntry("r-anya", "Аня", "anya", TrackingEnabled: true),
        });
        return state;
    }

    [Fact]
    public void Haversine_MatchesKnownDistance()
    {
        // One degree of latitude ≈ 111 km.
        var d = PresenceState.Haversine(0, 0, 1, 0);
        Assert.InRange(d, 110_500, 111_500);
    }

    [Fact]
    public void ReportInsideRadius_MakesResidentHome()
    {
        var state = WithAnya();
        var now = DateTimeOffset.UtcNow;

        // ~30 m north of home — well inside the 150 m fence.
        var changed = state.ApplyReport(AnyaKey, HomeLat + 0.0003, HomeLon, explicitPresent: null, battery: 90, now);

        Assert.Equal("r-anya", changed);
        Assert.True(state.Get("r-anya")!.Home);
        Assert.Equal(90, state.Get("r-anya")!.Battery);
        Assert.Equal((true, 1), state.Aggregate());
    }

    [Fact]
    public void DepartureWaitsOutTheGrace_ThenFlipsViaPending()
    {
        var state = WithAnya(graceSeconds: 300);
        var t0 = DateTimeOffset.UtcNow;

        // Arrive (home), then a far report starts the grace window but must NOT flip immediately.
        state.ApplyReport(AnyaKey, HomeLat, HomeLon, explicitPresent: null, battery: null, t0);
        var farAway = state.ApplyReport(AnyaKey, HomeLat + 0.05, HomeLon, explicitPresent: null, battery: null, t0.AddSeconds(10));

        Assert.Null(farAway);                       // still within grace → no change
        Assert.True(state.Get("r-anya")!.Home);

        // Before the grace elapses: pending, still home.
        Assert.Empty(state.EvaluatePending(t0.AddSeconds(120)));
        Assert.True(state.Get("r-anya")!.Home);

        // After the grace elapses: the tick flips it to away.
        var flipped = state.EvaluatePending(t0.AddSeconds(320));
        Assert.Equal(new[] { "r-anya" }, flipped);
        Assert.False(state.Get("r-anya")!.Home);
        Assert.Equal((false, 0), state.Aggregate());
    }

    [Fact]
    public void ArrivalIsImmediate_EvenWithinAPriorGrace()
    {
        var state = WithAnya(graceSeconds: 300);
        var t0 = DateTimeOffset.UtcNow;

        state.ApplyReport(AnyaKey, HomeLat, HomeLon, explicitPresent: null, battery: null, t0);
        state.ApplyReport(AnyaKey, HomeLat + 0.05, HomeLon, explicitPresent: null, battery: null, t0.AddSeconds(10)); // grace pending
        // Comes back inside before the grace elapses → immediately home, grace cleared.
        var back = state.ApplyReport(AnyaKey, HomeLat, HomeLon, explicitPresent: null, battery: null, t0.AddSeconds(60));

        Assert.Null(back);                          // was already home → no flip
        Assert.True(state.Get("r-anya")!.Home);
        Assert.Empty(state.EvaluatePending(t0.AddSeconds(400))); // grace was cleared, stays home
        Assert.True(state.Get("r-anya")!.Home);
    }

    [Fact]
    public void ExplicitTransition_BypassesGeofence()
    {
        var state = WithAnya();
        var now = DateTimeOffset.UtcNow;

        // No coordinates, but an explicit "entered home" transition → home.
        var enter = state.ApplyReport(AnyaKey, lat: null, lon: null, explicitPresent: true, battery: null, now);
        Assert.Equal("r-anya", enter);
        Assert.True(state.Get("r-anya")!.Home);

        // Explicit leave flips immediately (a trusted transition doesn't wait out the grace).
        var leave = state.ApplyReport(AnyaKey, lat: null, lon: null, explicitPresent: false, battery: null, now.AddSeconds(1));
        Assert.Equal("r-anya", leave);
        Assert.False(state.Get("r-anya")!.Home);
    }

    [Fact]
    public void UnknownKey_IsDropped()
    {
        var state = WithAnya();
        var changed = state.ApplyReport(new[] { "stranger" }, HomeLat, HomeLon, null, null, DateTimeOffset.UtcNow);
        Assert.Null(changed);
        Assert.Null(state.Get("r-anya"));
    }

    [Fact]
    public void UntrackedResident_NotCountedInAggregate()
    {
        var state = new PresenceState();
        state.Configure(150, 180, HomeLat, HomeLon);
        state.SyncRoster(new[]
        {
            new PresenceState.ResidentEntry("r-guest", "Гость", "guest", TrackingEnabled: false),
        });

        var changed = state.ApplyReport(new[] { "guest" }, HomeLat, HomeLon, null, null, DateTimeOffset.UtcNow);
        Assert.Null(changed);                       // untracked → report ignored
        Assert.Equal((false, 0), state.Aggregate());
    }

    [Fact]
    public void SyncRoster_ReportsAddedAndRemoved_AndDropsGonePresence()
    {
        var state = WithAnya();
        state.ApplyReport(AnyaKey, HomeLat, HomeLon, null, null, DateTimeOffset.UtcNow);
        Assert.True(state.Get("r-anya")!.Home);

        var (added, removed) = state.SyncRoster(new[]
        {
            new PresenceState.ResidentEntry("r-boris", "Борис", "boris", true),
        });

        Assert.Equal(new[] { "r-boris" }, added);
        Assert.Equal(new[] { "r-anya" }, removed);
        Assert.Null(state.Get("r-anya"));           // presence for the departed resident is dropped
    }
}
