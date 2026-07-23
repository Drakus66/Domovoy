// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;

namespace Domovoy.Contracts.Home;

/// <summary>
/// Pure per-device energy math (roadmap Epic 3C-D) — shared by the AutomationService runtime that publishes a
/// device's estimated <c>power</c>/<c>energy</c> and by the load coordinator that needs the same numbers to plan
/// a shed. Deliberately parameterized with plain doubles (not a profile DTO): the read-model document and the
/// service-side snapshot are separate types by convention, but they must estimate identically.
/// </summary>
public static class EnergyModel
{
    /// <summary>A single integration step is capped at this many hours: after a long offline/paused gap the step
    /// is skipped rather than adding <c>power × gap</c>, which would spike the cumulative total.</summary>
    public const double MaxGapHours = 1.0;

    /// <summary>
    /// Estimated instantaneous draw (W) for a device with no power meter. Off ⇒ <paramref name="standbyPowerW"/>.
    /// On with a bound regulator (brightness / fan speed / valve position, 0..100 %) ⇒ linear between
    /// <paramref name="minPowerW"/> and <paramref name="maxPowerW"/>. On with no regulator ⇒ full
    /// <paramref name="maxPowerW"/>.
    /// </summary>
    public static double EstimatePowerW(
        double maxPowerW, double minPowerW, double standbyPowerW, bool isOn, double? scalePercent)
    {
        if (!isOn) return Math.Max(0, standbyPowerW);

        var max = Math.Max(0, maxPowerW);
        var min = Math.Clamp(minPowerW, 0, max);
        if (scalePercent is not { } pct) return max;

        return min + (max - min) * (Math.Clamp(pct, 0, 100) / 100.0);
    }

    /// <summary>
    /// Accumulate an instantaneous power (W) held over <paramref name="elapsed"/> into a cumulative energy total
    /// (kWh). A non-positive or over-<see cref="MaxGapHours"/> step contributes nothing, so a restart or an
    /// offline stretch resumes the counter instead of inventing consumption for the gap.
    /// </summary>
    public static double Accumulate(double kwh, double powerW, TimeSpan elapsed)
    {
        var hours = elapsed.TotalHours;
        if (hours <= 0 || hours >= MaxGapHours) return kwh;
        return kwh + Math.Max(0, powerW) * hours / 1000.0; // W·h → kWh
    }

    /// <summary>
    /// Typical full-load draw (W) by semantic archetype (Epic 2D) — only a starting point the UI pre-fills when
    /// the user first enables accounting on a device with no meter; every value is meant to be corrected by hand.
    /// Unknown/undecidable archetypes return null rather than a misleading guess.
    /// </summary>
    public static double? DefaultPowerW(string? archetype) => archetype switch
    {
        DeviceArchetypes.Light => 9,
        DeviceArchetypes.Thermostat => 1000,   // convector / electric heater
        DeviceArchetypes.Valve => 5,
        DeviceArchetypes.Switch => 60,
        _ => null,
    };
}
