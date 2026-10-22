// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Home;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the per-device energy math (roadmap Epic 3C-D) that replaced the Powercalc/integrator blocks:
/// the linear estimate between min and max draw, the standby floor, and the kWh integration with its long-gap
/// guard (an offline stretch must not be billed at the last known draw).
/// </summary>
public sealed class EnergyModelTests
{
    [Fact]
    public void EstimatePowerW_ScalesLinearlyBetweenMinAndMax()
    {
        // 10 W at 0 %, 100 W at 100 % → 55 W at half brightness.
        Assert.Equal(55, EnergyModel.EstimatePowerW(100, 10, 1, isOn: true, scalePercent: 50), 3);
        Assert.Equal(10, EnergyModel.EstimatePowerW(100, 10, 1, isOn: true, scalePercent: 0), 3);
        Assert.Equal(100, EnergyModel.EstimatePowerW(100, 10, 1, isOn: true, scalePercent: 100), 3);
    }

    [Fact]
    public void EstimatePowerW_NoRegulator_DrawsFullLoad()
    {
        Assert.Equal(2000, EnergyModel.EstimatePowerW(2000, 0, 5, isOn: true, scalePercent: null), 3);
    }

    [Fact]
    public void EstimatePowerW_Off_UsesStandby()
    {
        // The regulator value is irrelevant while the device is off.
        Assert.Equal(2, EnergyModel.EstimatePowerW(100, 10, 2, isOn: false, scalePercent: 80), 3);
    }

    [Fact]
    public void EstimatePowerW_ClampsNonsensicalInputs()
    {
        Assert.Equal(0, EnergyModel.EstimatePowerW(-100, -10, -1, isOn: true, scalePercent: 50), 3);
        // A regulator above 100 % is capped rather than extrapolated.
        Assert.Equal(100, EnergyModel.EstimatePowerW(100, 0, 0, isOn: true, scalePercent: 250), 3);
        // A min above max collapses to max instead of inverting the ramp.
        Assert.Equal(60, EnergyModel.EstimatePowerW(60, 900, 0, isOn: true, scalePercent: 10), 3);
    }

    [Fact]
    public void Accumulate_AddsWattHoursOverElapsedTime()
    {
        var kwh = EnergyModel.Accumulate(0, powerW: 1000, TimeSpan.FromMinutes(30)); // 1 kW × 0.5 h
        Assert.Equal(0.5, kwh, 6);
        Assert.Equal(1.0, EnergyModel.Accumulate(kwh, 1000, TimeSpan.FromMinutes(30)), 6);
    }

    [Fact]
    public void Accumulate_SkipsLongGapAndNonPositiveSteps()
    {
        // Beyond the guard: the device was offline/paused, so the step contributes nothing (no spike).
        Assert.Equal(3.5, EnergyModel.Accumulate(3.5, 1000, TimeSpan.FromHours(5)), 6);
        Assert.Equal(3.5, EnergyModel.Accumulate(3.5, 1000, TimeSpan.Zero), 6);
        Assert.Equal(3.5, EnergyModel.Accumulate(3.5, 1000, TimeSpan.FromMinutes(-10)), 6);
    }

    [Fact]
    public void DefaultPowerW_KnownArchetypesOnly()
    {
        Assert.Equal(9, EnergyModel.DefaultPowerW(DeviceArchetypes.Light));
        Assert.Equal(1000, EnergyModel.DefaultPowerW(DeviceArchetypes.Thermostat));
        Assert.Null(EnergyModel.DefaultPowerW(DeviceArchetypes.Motion));
        Assert.Null(EnergyModel.DefaultPowerW(null));
    }
}
