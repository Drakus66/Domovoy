using System.Globalization;

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
/// Hysteresis thermostat (controller class, roadmap Epic 1H). Produces a heating <c>on_off</c> demand
/// from a temperature input and a setpoint, with a dead-band so it doesn't chatter around the target.
/// The setpoint is a <b>writable</b> output capability — commanding the virtual device's
/// <c>temperature_setpoint</c> retargets the loop live (the hook for user setpoints and Phase-2 ML).
/// Bind its <c>temperature</c> input to a raw sensor or, better, to an EWMA filter's output.
/// </summary>
public sealed class ThermostatType : IBlockType
{
    public string TypeId => "thermostat";
    public string Title => "Thermostat (hysteresis)";
    public string Description => "Bang-bang heating demand from a temperature input and a setpoint, with hysteresis.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("temperature", CapabilityKind.Number, "Measured (ideally filtered) temperature"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.OnOff(writable: false),                       // heating demand (read)
        WellKnownCapabilities.TemperatureSetpoint(min: 5, max: 35, step: 0.5), // commandable target
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("setpoint", 21, "°C", 5, 35, "Target temperature (initial; commandable live)"),
        new BlockParamSpec("hysteresis", 0.5, "°C", 0.1, 5, "Dead-band around the setpoint"),
    };

    public IBlock Create() => new ThermostatBlock();
}

public sealed class ThermostatBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        // A live command to temperature_setpoint overrides the configured default.
        var setpoint = AsDouble(ctx.Commanded(CapabilityIds.TemperatureSetpoint)) ?? ctx.Param("setpoint", 21);
        var hysteresis = Math.Max(0.1, ctx.Param("hysteresis", 0.5));

        ctx.Emit(CapabilityIds.TemperatureSetpoint, setpoint); // reflect the effective setpoint as state

        var temp = ctx.ReadNumber("temperature");
        if (temp is null) return;

        var demand = ctx.GetState<bool>("demand");
        if (temp.Value < setpoint - hysteresis) demand = true;
        else if (temp.Value > setpoint + hysteresis) demand = false;
        // within the dead-band: hold the previous demand

        ctx.SetState("demand", demand);
        ctx.Emit(CapabilityIds.OnOff, demand);
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
