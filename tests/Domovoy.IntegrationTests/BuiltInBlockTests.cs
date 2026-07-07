// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the deterministic control blocks extended in Epic 1D: the thermostat's heat / cool /
/// heat-cool modes and the <c>sun_gate</c> outdoor-lighting block. Pure — no infrastructure.
/// </summary>
public sealed class BuiltInBlockTests
{
    // --- Thermostat: heating (mode 0, back-compatible default) -----------------------------------

    [Fact]
    public void Thermostat_Heat_DemandsBelowSetpoint_NoCooling()
    {
        var ctx = TickThermostat(mode: 0, temp: 18, setpoint: 21);

        Assert.Equal(true, ctx.Get(CapabilityIds.OnOff));       // heating on
        Assert.Equal(false, ctx.Get(CapabilityIds.CoolDemand)); // never cools in heat mode
    }

    [Fact]
    public void Thermostat_Heat_ReleasesAboveSetpoint()
    {
        var ctx = TickThermostat(mode: 0, temp: 23, setpoint: 21);
        Assert.Equal(false, ctx.Get(CapabilityIds.OnOff));
    }

    // --- Thermostat: cooling (mode 1) ------------------------------------------------------------

    [Fact]
    public void Thermostat_Cool_DemandsAboveSetpoint_NoHeating()
    {
        var ctx = TickThermostat(mode: 1, temp: 25, setpoint: 21);

        Assert.Equal(false, ctx.Get(CapabilityIds.OnOff));     // never heats in cool mode
        Assert.Equal(true, ctx.Get(CapabilityIds.CoolDemand)); // cooling on
    }

    [Fact]
    public void Thermostat_Cool_ReleasesBelowSetpoint()
    {
        var ctx = TickThermostat(mode: 1, temp: 18, setpoint: 21);
        Assert.Equal(false, ctx.Get(CapabilityIds.CoolDemand));
    }

    // --- Thermostat: heat-cool (mode 2) with a neutral dead-band ---------------------------------

    [Fact]
    public void HeatCool_HeatsBelowSetpoint()
    {
        var ctx = TickThermostat(mode: 2, temp: 18, setpoint: 21, coolSetpoint: 25);
        Assert.Equal(true, ctx.Get(CapabilityIds.OnOff));
        Assert.Equal(false, ctx.Get(CapabilityIds.CoolDemand));
    }

    [Fact]
    public void HeatCool_CoolsAboveCoolSetpoint()
    {
        var ctx = TickThermostat(mode: 2, temp: 27, setpoint: 21, coolSetpoint: 25);
        Assert.Equal(false, ctx.Get(CapabilityIds.OnOff));
        Assert.Equal(true, ctx.Get(CapabilityIds.CoolDemand));
    }

    [Fact]
    public void HeatCool_NeutralBand_NeitherHeatsNorCools()
    {
        // 23 sits inside [21, 25] — the dead-band between heating and cooling.
        var ctx = TickThermostat(mode: 2, temp: 23, setpoint: 21, coolSetpoint: 25);
        Assert.Equal(false, ctx.Get(CapabilityIds.OnOff));
        Assert.Equal(false, ctx.Get(CapabilityIds.CoolDemand));
    }

    // --- Sun gate --------------------------------------------------------------------------------

    [Fact]
    public void SunGate_On_AfterSunset_Off_AtSolarNoon()
    {
        // Derive the test instants from the same calculator so the block's decision is unambiguous,
        // independent of the exact site geometry.
        var sun = new SunCalculator(55.7558, 37.6173);
        var block = new SunGateBlock(sun);
        var (sunrise, sunset) = sun.ForDate(new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);

        var afterSunset = new FakeBlockContext { Now = new DateTimeOffset(sunset!.Value.AddHours(1), TimeSpan.Zero) };
        block.Tick(afterSunset);
        Assert.Equal(true, afterSunset.Get(CapabilityIds.OnOff)); // dark

        var solarNoon = sunrise!.Value + (sunset.Value - sunrise.Value) / 2;
        var noon = new FakeBlockContext { Now = new DateTimeOffset(solarNoon, TimeSpan.Zero) };
        block.Tick(noon);
        Assert.Equal(false, noon.Get(CapabilityIds.OnOff)); // light
    }

    [Fact]
    public void SunGate_Offset_LeadsDusk()
    {
        // With a 30-min lead, the block reads "dark" 15 min before sunset (inside the offset), but not
        // with zero offset.
        var sun = new SunCalculator(55.7558, 37.6173);
        var block = new SunGateBlock(sun);
        var (_, sunset) = sun.ForDate(new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
        var justBeforeSunset = new DateTimeOffset(sunset!.Value.AddMinutes(-15), TimeSpan.Zero);

        var noLead = new FakeBlockContext { Now = justBeforeSunset };
        block.Tick(noLead);
        Assert.Equal(false, noLead.Get(CapabilityIds.OnOff));

        var withLead = new FakeBlockContext { Now = justBeforeSunset, Params = { ["offsetMinutes"] = 30 } };
        block.Tick(withLead);
        Assert.Equal(true, withLead.Get(CapabilityIds.OnOff));
    }

    private static FakeBlockContext TickThermostat(
        int mode, double temp, double setpoint, double coolSetpoint = 24)
    {
        var block = new ThermostatBlock();
        var ctx = new FakeBlockContext
        {
            Params =
            {
                ["mode"] = mode, ["setpoint"] = setpoint,
                ["coolSetpoint"] = coolSetpoint, ["hysteresis"] = 0.5,
            },
            Inputs = { ["temperature"] = temp },
        };
        block.Tick(ctx);
        return ctx;
    }

    /// <summary>Minimal in-memory <see cref="IBlockContext"/> for unit-testing a block in isolation.</summary>
    private sealed class FakeBlockContext : IBlockContext
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public string? ZoneId { get; set; }
        public string? ZoneKind { get; set; }
        public Dictionary<string, double> Params { get; } = new();
        public Dictionary<string, object?> Inputs { get; } = new();
        public Dictionary<string, object?> Commands { get; } = new();
        public Dictionary<string, object?> State { get; } = new();
        public Dictionary<string, object?> Emitted { get; } = new();

        public object? Get(string cap) => Emitted.TryGetValue(cap, out var v) ? v : null;

        public object? Read(string inputPort) => Inputs.TryGetValue(inputPort, out var v) ? v : null;
        public double? ReadNumber(string inputPort) => Read(inputPort) is double d ? d : null;
        public double Param(string key, double fallback) => Params.TryGetValue(key, out var v) ? v : fallback;
        public object? Commanded(string capabilityId) => Commands.TryGetValue(capabilityId, out var v) ? v : null;
        public void Emit(string capabilityId, object? value) => Emitted[capabilityId] = value;
        public T? GetState<T>(string key) => State.TryGetValue(key, out var v) && v is T t ? t : default;
        public void SetState<T>(string key, T value) => State[key] = value;
        public void Log(string message) { }
    }
}
