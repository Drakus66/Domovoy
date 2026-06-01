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
