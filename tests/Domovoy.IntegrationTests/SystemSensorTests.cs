// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the system virtual Sun sensor (roadmap Epic 2L): the NOAA solar-position math and the
/// pure <see cref="SystemSensorService.ComputeSun"/> snapshot. No infrastructure — the bus is never touched
/// by <c>ComputeSun</c>.
/// </summary>
public sealed class SystemSensorTests
{
    // --- Solar position (elevation/azimuth) -----------------------------------------------------

    [Fact]
    public void Position_EquatorEquinoxNoon_SunNearZenith()
    {
        var sun = new SunCalculator(0, 0);
        var (elevation, _) = sun.Position(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));
        Assert.True(elevation > 80, $"expected sun near zenith at equinox noon, got {elevation:F1}°");
    }

    [Fact]
    public void Position_EquatorEquinoxMidnight_SunFarBelowHorizon()
    {
        var sun = new SunCalculator(0, 0);
        var (elevation, _) = sun.Position(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero));
        Assert.True(elevation < -80, $"expected sun far below horizon at equinox midnight, got {elevation:F1}°");
    }

    [Fact]
    public void Position_MoscowSummerSolarNoon_HighAndDueSouth()
    {
        // Moscow (55.7558°N, 37.6173°E) at the June solstice, ~solar noon (≈09:28 UTC for this longitude).
        var sun = new SunCalculator(55.7558, 37.6173);
        var (elevation, azimuth) = sun.Position(new DateTimeOffset(2026, 6, 21, 9, 28, 0, TimeSpan.Zero));

        // Max elevation ≈ 90 − lat + 23.44 ≈ 57.7°; azimuth due south (180°) at solar noon.
        Assert.InRange(elevation, 50, 58);
        Assert.InRange(azimuth, 165, 195);
    }

    // --- ComputeSun snapshot --------------------------------------------------------------------

    [Fact]
    public void ComputeSun_MapsCapabilities_AndIsDayTracksElevation()
    {
        var service = NewService(new SunCalculator(0, 0), new SiteContext());

        var day = service.ComputeSun(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));
        var state = day.ToState();

        Assert.True((double)state[CapabilityIds.SunElevation]! > 80);
        Assert.Equal(day.Elevation > 0, state[CapabilityIds.IsDay]);
        Assert.True(day.IsDay);
        Assert.False(day.IsDark);
        // Sunrise/sunset are formatted local HH:mm (equator equinox → ~06:00 / ~18:00 UTC).
        Assert.Matches(@"^\d{2}:\d{2}$", day.Sunrise);
        Assert.Matches(@"^\d{2}:\d{2}$", day.Sunset);

        var night = service.ComputeSun(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero));
        Assert.False(night.IsDay);
        Assert.True(night.IsDark);
    }

    // --- Time sensor ----------------------------------------------------------------------------

    [Fact]
    public void ComputeTime_LocalMinutesAndClock()
    {
        var service = NewService(new SunCalculator(0, 0), new SiteContext()); // default tz = UTC
        var time = service.ComputeTime(new DateTimeOffset(2026, 7, 6, 9, 30, 0, TimeSpan.Zero));

        Assert.Equal(9 * 60 + 30, time.TimeOfDay);
        Assert.Equal("09:30", time.Clock);
    }

    // --- Calendar sensor ------------------------------------------------------------------------

    [Fact]
    public void ComputeCalendar_WeekendAndHolidayFlags()
    {
        var calendar = new CalendarContext(); // default weekend = Sat+Sun, no holidays
        calendar.Update(new[] { 6, 0 }, new[] { "2026-07-04" }); // mark 4 Jul (a Saturday) a holiday
        var service = NewService(new SunCalculator(0, 0), new SiteContext(), calendar);

        var saturday = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero); // Saturday + holiday
        var sat = service.ComputeCalendar(saturday);
        Assert.Equal("Saturday", sat.DayOfWeek);
        Assert.True(sat.IsWeekend);
        Assert.True(sat.IsHoliday);
        Assert.Equal("2026-07-04", sat.Date);

        var monday = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero); // Monday, ordinary day
        var mon = service.ComputeCalendar(monday);
        Assert.Equal("Monday", mon.DayOfWeek);
        Assert.False(mon.IsWeekend);
        Assert.False(mon.IsHoliday);
    }

    private static SystemSensorService NewService(SunCalculator sun, SiteContext site, CalendarContext? calendar = null) =>
        // Compute* methods never touch the bus, so a null bus is safe for these pure-computation tests.
        new(bus: null!, sun, site, calendar ?? new CalendarContext(), NullLogger<SystemSensorService>.Instance);
}
