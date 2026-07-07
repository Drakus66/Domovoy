// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// EWMA filter (estimator class, roadmap Epic 1H). Smooths a noisy numeric signal with an exponentially-
/// weighted moving average whose responsiveness is set by a time constant <c>tau</c>. Time-aware: the
/// blend factor adapts to the actual gap between ticks, so irregular sampling still smooths correctly.
/// Output capability <c>value</c> — a downstream block (e.g. a thermostat) can bind to it (composition).
/// </summary>
public sealed class EwmaFilterType : IBlockType
{
    public string TypeId => "ewma_filter";
    public string Title => "EWMA filter";
    public string Description => "Exponentially-weighted moving average — smooths a noisy numeric signal.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Raw numeric signal to smooth"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number("value", writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("tau", 300, "s", 1, 3600, "Smoothing time constant — larger = smoother/slower"),
    };

    public IBlock Create() => new EwmaFilterBlock();
}

public sealed class EwmaFilterBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return; // no input yet

        var tau = Math.Max(1, ctx.Param("tau", 300));

        double y;
        if (!ctx.GetState<bool>("seeded"))
        {
            y = x.Value; // seed on first sample
        }
        else
        {
            var prev = ctx.GetState<double>("y");
            var last = ctx.GetState<DateTimeOffset>("t");
            var dt = Math.Max(0, (ctx.Now - last).TotalSeconds);
            var alpha = 1 - Math.Exp(-dt / tau); // time-aware blend factor
            y = prev + alpha * (x.Value - prev);
        }

        ctx.SetState("y", y);
        ctx.SetState("t", ctx.Now);
        ctx.SetState("seeded", true);
        ctx.Emit("value", Math.Round(y, 3));
    }
}

/// <summary>
/// Hysteresis thermostat (controller class, roadmap Epic 1H/1D). Produces a heating <c>on_off</c> demand
/// and — in cooling / heat-cool modes — a companion <c>cool_demand</c> from a temperature input and a
/// setpoint, each with a dead-band so it doesn't chatter around the target. The setpoint is a
/// <b>writable</b> output capability — commanding the virtual device's <c>temperature_setpoint</c>
/// retargets the loop live (the hook for user setpoints and Phase-2 ML). Bind its <c>temperature</c>
/// input to a raw sensor or, better, to an EWMA filter's output.
/// <para>
/// <b>Mode</b> (numeric param, since <c>ControlBlock.Params</c> is numeric-only): 0 = heat (default,
/// back-compatible — cooling stays off), 1 = cool (invert: demand rises above the setpoint, carried on
/// <c>cool_demand</c>), 2 = heat-cool (heat below <c>setpoint</c>, cool above <c>coolSetpoint</c>, with a
/// neutral dead-band between). Bind <c>on_off</c> to the heater and <c>cool_demand</c> to the cooler.
/// </para>
/// </summary>
public sealed class ThermostatType : IBlockType
{
    public string TypeId => "thermostat";
    public string Title => "Thermostat (hysteresis)";
    public string Description => "Bang-bang heating/cooling demand from a temperature input and a setpoint, with hysteresis.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("temperature", CapabilityKind.Number, "Measured (ideally filtered) temperature"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.OnOff(writable: false),                          // heating demand (read)
        WellKnownCapabilities.Boolean(CapabilityIds.CoolDemand, writable: false), // cooling demand (read)
        WellKnownCapabilities.TemperatureSetpoint(min: 5, max: 35, step: 0.5), // commandable target
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("setpoint", 21, "°C", 5, 35, "Target temperature (initial; commandable live)"),
        new BlockParamSpec("hysteresis", 0.5, "°C", 0.1, 5, "Dead-band around the setpoint"),
        new BlockParamSpec("mode", 0, null, 0, 2, "0 = heat, 1 = cool, 2 = heat-cool"),
        new BlockParamSpec("coolSetpoint", 24, "°C", 5, 40, "Upper (cooling) target — used only in heat-cool mode"),
    };

    public IBlock Create() => new ThermostatBlock();
}

public sealed class ThermostatBlock : IBlock
{
    private const int ModeHeat = 0;
    private const int ModeCool = 1;
    private const int ModeHeatCool = 2;

    public void Tick(IBlockContext ctx)
    {
        // A live command to temperature_setpoint overrides the configured default.
        var setpoint = AsDouble(ctx.Commanded(CapabilityIds.TemperatureSetpoint)) ?? ctx.Param("setpoint", 21);
        var hysteresis = Math.Max(0.1, ctx.Param("hysteresis", 0.5));
        var mode = (int)Math.Round(ctx.Param("mode", ModeHeat));
        // In heat-cool the cooling target is a separate param; never let it fall below the heating setpoint.
        var coolSetpoint = Math.Max(setpoint, ctx.Param("coolSetpoint", 24));

        ctx.Emit(CapabilityIds.TemperatureSetpoint, setpoint); // reflect the effective setpoint as state

        var temp = ctx.ReadNumber("temperature");
        if (temp is null) return;

        var heat = ctx.GetState<bool>("demand");
        var cool = ctx.GetState<bool>("cool");

        if (mode != ModeCool)
        {
            // Heating leg (heat + heat-cool): on below setpoint-hyst, off above setpoint+hyst.
            if (temp.Value < setpoint - hysteresis) heat = true;
            else if (temp.Value > setpoint + hysteresis) heat = false;
        }
        else heat = false;

        if (mode != ModeHeat)
        {
            // Cooling leg (cool + heat-cool): in heat-cool the cooling target is coolSetpoint, else the setpoint.
            var coolTarget = mode == ModeHeatCool ? coolSetpoint : setpoint;
            if (temp.Value > coolTarget + hysteresis) cool = true;
            else if (temp.Value < coolTarget - hysteresis) cool = false;
        }
        else cool = false;

        ctx.SetState("demand", heat);
        ctx.SetState("cool", cool);
        ctx.Emit(CapabilityIds.OnOff, heat);
        ctx.Emit(CapabilityIds.CoolDemand, cool);
    }

    private static double? AsDouble(object? v) => v switch
    {
        null => null,
        double d => d,
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };
}

/// <summary>
/// CO₂ ventilation controller (climate domain, roadmap Epic 1D). Calls for ventilation when measured CO₂
/// rises above a threshold and releases it once CO₂ falls back below, with a dead-band so a fan near the
/// threshold doesn't chatter. Bind its <c>on_off</c> output to a ventilation relay/fan.
/// </summary>
public sealed class Co2VentilationType : IBlockType
{
    public string TypeId => "co2_ventilation";
    public string Title => "CO₂ ventilation";
    public string Description => "Demands ventilation when CO₂ rises above a threshold (with hysteresis).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("co2", CapabilityKind.Number, "Measured CO₂ (ppm)"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.OnOff(writable: false), // ventilation demand
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("threshold", 1000, "ppm", 400, 3000, "CO₂ level that calls for ventilation"),
        new BlockParamSpec("hysteresis", 150, "ppm", 10, 1000, "Dead-band around the threshold"),
    };

    public IBlock Create() => new Co2VentilationBlock();
}

public sealed class Co2VentilationBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var co2 = ctx.ReadNumber("co2");
        if (co2 is null) return;

        var threshold = ctx.Param("threshold", 1000);
        var hysteresis = Math.Max(1, ctx.Param("hysteresis", 150));

        var demand = ctx.GetState<bool>("demand");
        if (co2.Value > threshold + hysteresis) demand = true;
        else if (co2.Value < threshold - hysteresis) demand = false;

        ctx.SetState("demand", demand);
        ctx.Emit(CapabilityIds.OnOff, demand);
    }
}

/// <summary>
/// Irrigation cycle / sequencer (grounds domain, roadmap Epic 1D). A small state machine that opens a
/// valve for <c>runMinutes</c> every <c>intervalHours</c>. An optional <c>inhibit</c> input (e.g. a rain
/// sensor or a wet-soil reading) skips a cycle while it is non-zero — the hook for rain/ET correction.
/// Bind its <c>on_off</c> output to a valve. Multi-zone sequencing is a composite (E2) of several of these.
/// </summary>
public sealed class IrrigationSequencerType : IBlockType
{
    public string TypeId => "irrigation_sequencer";
    public string Title => "Irrigation cycle";
    public string Description => "Opens a valve for a run time on a fixed interval; an inhibit input (rain) skips a cycle.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("inhibit", CapabilityKind.Number, "Optional: non-zero skips watering (e.g. rain)"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.OnOff(writable: false), // valve demand
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("intervalHours", 24, "h", 0.1, 720, "Time between watering cycles"),
        new BlockParamSpec("runMinutes", 15, "min", 1, 240, "How long the valve stays open each cycle"),
    };

    public IBlock Create() => new IrrigationSequencerBlock();
}

public sealed class IrrigationSequencerBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var now = ctx.Now;

        // Watering state: keep the valve open until the run time elapses.
        if (ctx.GetState<bool>("watering"))
        {
            var started = ctx.GetState<DateTimeOffset>("wstart");
            var runMinutes = Math.Max(1, ctx.Param("runMinutes", 15));
            if ((now - started).TotalMinutes >= runMinutes)
            {
                ctx.SetState("watering", false);
                ctx.SetState("lastRun", now);
                ctx.Emit(CapabilityIds.OnOff, false);
            }
            else
            {
                ctx.Emit(CapabilityIds.OnOff, true);
            }
            return;
        }

        // Idle: seed the clock on first tick so the first cycle waits a full interval.
        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("lastRun", now);
            ctx.SetState("seeded", true);
        }

        // Rain/ET inhibit: hold the valve closed while the inhibit input is truthy.
        if (ctx.ReadNumber("inhibit") is > 0)
        {
            ctx.Emit(CapabilityIds.OnOff, false);
            return;
        }

        var lastRun = ctx.GetState<DateTimeOffset>("lastRun");
        var intervalHours = Math.Max(0.1, ctx.Param("intervalHours", 24));
        if ((now - lastRun).TotalHours >= intervalHours)
        {
            ctx.SetState("watering", true);
            ctx.SetState("wstart", now);
            ctx.Emit(CapabilityIds.OnOff, true);
        }
        else
        {
            ctx.Emit(CapabilityIds.OnOff, false);
        }
    }
}

/// <summary>
/// Sun-gate for outdoor lighting (grounds domain, roadmap Epic 1D "наружное освещение по sun"). Emits an
/// <c>on_off</c> demand that is true while it is dark at the configured site, so binding outdoor lights to
/// it drives them dusk-to-dawn — with the block's history/telemetry for free (virtual device). A positive
/// <c>offsetMinutes</c> leads dusk and trails dawn (lights come on before sunset, go off after sunrise).
/// Pure sun geometry (offline). For conditional on/off (e.g. only when away) compose with a rule (1A).
/// </summary>
public sealed class SunGateType : IBlockType
{
    private readonly SunCalculator _sun;

    public SunGateType(SunCalculator sun) => _sun = sun;

    public string TypeId => "sun_gate";
    public string Title => "Sun gate (outdoor lighting)";
    public string Description => "On while it is dark at the site (dusk-to-dawn), with a lead/trail offset — for outdoor lights.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = Array.Empty<BlockPortSpec>();

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.OnOff(writable: false), // "it is dark" demand
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("offsetMinutes", 0, "min", 0, 120, "Turn on this many minutes before sunset and off after sunrise"),
    };

    public IBlock Create() => new SunGateBlock(_sun);
}

public sealed class SunGateBlock : IBlock
{
    private readonly SunCalculator _sun;

    public SunGateBlock(SunCalculator sun) => _sun = sun;

    public void Tick(IBlockContext ctx)
    {
        var offset = Math.Max(0, ctx.Param("offsetMinutes", 0));
        ctx.Emit(CapabilityIds.OnOff, _sun.IsDark(ctx.Now, offset));
    }
}
