// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Generalized primitive blocks (roadmap Epic 2Q). Unlike the domain blocks (thermostat, co2_ventilation, …)
/// each of these does one small, reusable thing — compare, threshold with hysteresis, map, clamp, combine —
/// on any numeric/boolean signal, and its output binds to a device or feeds another block. Domain behaviour is
/// built by composing these (e.g. a thermostat = <c>setpoint → hysteresis</c>), rather than baking it into a
/// bespoke type. Primitives that need a non-numeric knob (an operator, a mode) use the typed
/// <see cref="IBlockContext.Option"/> channel.
///
/// <para>Output convention: a numeric primitive emits <c>value</c> (Number), a boolean one emits <c>state</c>
/// (Boolean). The runtime publishes these as the block's virtual-device capabilities.</para>
/// </summary>
internal static class GenericBlockConventions
{
    public const string ValueOut = "value";
    public const string StateOut = "state";

    /// <summary>Read a bound input as a tri-state boolean: null when unbound, else truthy per <see cref="ValueOps.AsBool"/>.</summary>
    public static bool? ReadBool(IBlockContext ctx, string port)
    {
        var v = ctx.Read(port);
        return v is null ? null : ValueOps.AsBool(v);
    }

    /// <summary>Read a boolean option (true/1/yes/on), falling back to <paramref name="dflt"/>.</summary>
    public static bool OptBool(IBlockContext ctx, string key, bool dflt)
    {
        var s = ctx.Option(key);
        return string.IsNullOrWhiteSpace(s)
            ? dflt
            : s.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
    }

    /// <summary>A threshold that may come from an (optional) input port, else a numeric param, else a default.</summary>
    public static double Threshold(IBlockContext ctx, string port, string paramKey, double dflt) =>
        ctx.ReadNumber(port) ?? ctx.Param(paramKey, dflt);
}

// ── Comparator ──────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Compares an input against a threshold and emits a boolean (roadmap Epic 2Q). The threshold may be a fixed
/// param or a live second input (e.g. compare a sensor against a setpoint block). The operator is a typed
/// option (gt/lt/gte/lte/eq/ne). Threshold-crossing was previously re-hand-coded inside every domain block.
/// </summary>
public sealed class ComparatorType : IBlockType
{
    public string TypeId => "comparator";
    public string Category => BlockCategories.Logic;
    public string Title => "Comparator";
    public string Description => "Emits true when the input satisfies an operator against a threshold.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Value to test"),
        new BlockPortSpec("threshold", CapabilityKind.Number, "Optional live threshold (overrides the param)", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Boolean(GenericBlockConventions.StateOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("threshold", 0, null, null, null, "Threshold (used when the threshold input is unbound)"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("op", BlockOptionKind.Enum, "gt", "Comparison operator",
            new[] { "gt", "lt", "gte", "lte", "eq", "ne" }),
    };

    public IBlock Create() => new ComparatorBlock();
}

public sealed class ComparatorBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var value = ctx.ReadNumber("in");
        if (value is null) return;

        var threshold = GenericBlockConventions.Threshold(ctx, "threshold", "threshold", 0);
        var op = ctx.Option("op") ?? "gt";
        ctx.Emit(GenericBlockConventions.StateOut, ValueOps.Compare(value.Value, op, threshold));
    }
}

// ── Hysteresis (generalized bang-bang) ──────────────────────────────────────────────────────────

/// <summary>
/// Generalized hysteresis / bang-bang element (roadmap Epic 2Q) — the reusable core of the thermostat, the
/// CO₂ fan and any two-threshold relay. Turns <c>state</c> on when the input rises above <c>high</c> and off
/// when it falls below <c>low</c>; between the two it holds (the dead-band that stops chatter). <c>invert</c>
/// flips the sense (on below <c>low</c>) for cooling-style loops. Thresholds can be live inputs (bound to
/// setpoint blocks) so a downstream setpoint retargets the loop without editing params.
/// </summary>
public sealed class HysteresisType : IBlockType
{
    public string TypeId => "hysteresis";
    public string Category => BlockCategories.Control;
    public string Title => "Hysteresis (relay)";
    public string Description => "Two-threshold on/off with a dead-band — the generic bang-bang controller.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Measured value"),
        new BlockPortSpec("high", CapabilityKind.Number, "Optional live upper threshold (overrides param)", Optional: true),
        new BlockPortSpec("low", CapabilityKind.Number, "Optional live lower threshold (overrides param)", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Boolean(GenericBlockConventions.StateOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("high", 1, null, null, null, "Turn ON above this (turn OFF above this when inverted)"),
        new BlockParamSpec("low", 0, null, null, null, "Turn OFF below this (turn ON below this when inverted)"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("invert", BlockOptionKind.Bool, "false", "Invert (on below low, off above high) — cooling-style"),
    };

    public IBlock Create() => new HysteresisBlock();
}

public sealed class HysteresisBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return;

        var high = GenericBlockConventions.Threshold(ctx, "high", "high", 1);
        var low = GenericBlockConventions.Threshold(ctx, "low", "low", 0);
        if (low > high) (low, high) = (high, low); // tolerate swapped thresholds
        var invert = GenericBlockConventions.OptBool(ctx, "invert", false);

        var on = ctx.GetState<bool>("on");
        if (!invert)
        {
            if (x.Value > high) on = true;
            else if (x.Value < low) on = false;
        }
        else
        {
            if (x.Value < low) on = true;
            else if (x.Value > high) on = false;
        }

        ctx.SetState("on", on);
        ctx.Emit(GenericBlockConventions.StateOut, on);
    }
}

// ── Window (in-range) ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Emits true while the input is within <c>[low, high]</c> (roadmap Epic 2Q); <c>invert</c> makes it true
/// while out of range. For band checks — comfortable-humidity, healthy-CO₂, on-target — without wiring two
/// comparators.
/// </summary>
public sealed class WindowType : IBlockType
{
    public string TypeId => "window";
    public string Category => BlockCategories.Logic;
    public string Title => "Window (in-range)";
    public string Description => "True while the input is between low and high (or outside, when inverted).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Value to test"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Boolean(GenericBlockConventions.StateOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("low", 0, null, null, null, "Lower bound (inclusive)"),
        new BlockParamSpec("high", 100, null, null, null, "Upper bound (inclusive)"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("invert", BlockOptionKind.Bool, "false", "Emit true when OUT of range instead"),
    };

    public IBlock Create() => new WindowBlock();
}

public sealed class WindowBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return;

        var low = ctx.Param("low", 0);
        var high = ctx.Param("high", 100);
        if (low > high) (low, high) = (high, low);

        var inRange = x.Value >= low && x.Value <= high;
        var invert = GenericBlockConventions.OptBool(ctx, "invert", false);
        ctx.Emit(GenericBlockConventions.StateOut, invert ? !inRange : inRange);
    }
}

// ── Logic gate ──────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boolean combination of up to four inputs (roadmap Epic 2Q): and/or/xor/nand/nor, with an optional final
/// invert. Unbound inputs are ignored, so a two-input AND is just <c>a</c>+<c>b</c> bound. This is how you gate
/// one block by another (e.g. irrigation AND NOT rain).
/// </summary>
public sealed class LogicType : IBlockType
{
    public string TypeId => "logic";
    public string Category => BlockCategories.Logic;
    public string Title => "Logic gate";
    public string Description => "Combines boolean inputs with and/or/xor/nand/nor (+ optional invert).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("a", CapabilityKind.Boolean, "First input"),
        new BlockPortSpec("b", CapabilityKind.Boolean, "Second input", Optional: true),
        new BlockPortSpec("c", CapabilityKind.Boolean, "Third input", Optional: true),
        new BlockPortSpec("d", CapabilityKind.Boolean, "Fourth input", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Boolean(GenericBlockConventions.StateOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("op", BlockOptionKind.Enum, "and", "Boolean operator",
            new[] { "and", "or", "xor", "nand", "nor" }),
        new BlockOptionSpec("invert", BlockOptionKind.Bool, "false", "Invert the result"),
    };

    public IBlock Create() => new LogicBlock();
}

public sealed class LogicBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var inputs = new[] { "a", "b", "c", "d" }
            .Select(p => GenericBlockConventions.ReadBool(ctx, p))
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
        if (inputs.Count == 0) return;

        var op = (ctx.Option("op") ?? "and").ToLowerInvariant();
        bool result = op switch
        {
            "or" => inputs.Any(v => v),
            "nor" => !inputs.Any(v => v),
            "xor" => inputs.Count(v => v) % 2 == 1,
            "nand" => !inputs.All(v => v),
            _ => inputs.All(v => v), // and
        };

        if (GenericBlockConventions.OptBool(ctx, "invert", false)) result = !result;
        ctx.Emit(GenericBlockConventions.StateOut, result);
    }
}

// ── Select (2:1 mux) ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Passes input <c>a</c> when the boolean <c>sel</c> is true, else <c>b</c> (roadmap Epic 2Q). Switch a
/// setpoint by mode (comfort vs eco), pick a sensor, override ML with a manual value, etc.
/// </summary>
public sealed class SelectType : IBlockType
{
    public string TypeId => "select";
    public string Category => BlockCategories.Logic;
    public string Title => "Select (2:1)";
    public string Description => "Outputs a when sel is true, otherwise b.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("sel", CapabilityKind.Boolean, "Selector"),
        new BlockPortSpec("a", CapabilityKind.Number, "Value when sel is true"),
        new BlockPortSpec("b", CapabilityKind.Number, "Value when sel is false"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number(GenericBlockConventions.ValueOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IBlock Create() => new SelectBlock();
}

public sealed class SelectBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var sel = GenericBlockConventions.ReadBool(ctx, "sel") ?? false;
        var chosen = sel ? ctx.ReadNumber("a") : ctx.ReadNumber("b");
        if (chosen is not null) ctx.Emit(GenericBlockConventions.ValueOut, chosen.Value);
    }
}

// ── Linear map (scale + offset) ─────────────────────────────────────────────────────────────────

/// <summary>Linear transform <c>y = in·gain + offset</c> (roadmap Epic 2Q) — normalize a sensor, convert units, drive a dimmer from a percentage.</summary>
public sealed class LinearMapType : IBlockType
{
    public string TypeId => "linear_map";
    public string Category => BlockCategories.Math;
    public string Title => "Linear map (gain/offset)";
    public string Description => "y = in × gain + offset.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Input value"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number(GenericBlockConventions.ValueOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("gain", 1, null, null, null, "Multiplier"),
        new BlockParamSpec("offset", 0, null, null, null, "Added after scaling"),
    };

    public IBlock Create() => new LinearMapBlock();
}

public sealed class LinearMapBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return;
        var y = x.Value * ctx.Param("gain", 1) + ctx.Param("offset", 0);
        ctx.Emit(GenericBlockConventions.ValueOut, Math.Round(y, 4));
    }
}

// ── Clamp (limiter) ─────────────────────────────────────────────────────────────────────────────

/// <summary>Clamps the input to <c>[min, max]</c> (roadmap Epic 2Q) — a safety limiter as a standalone block.</summary>
public sealed class ClampType : IBlockType
{
    public string TypeId => "clamp";
    public string Category => BlockCategories.Math;
    public string Title => "Clamp (limiter)";
    public string Description => "Constrains the input to a [min, max] range.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Input value"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number(GenericBlockConventions.ValueOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("min", 0, null, null, null, "Lower bound"),
        new BlockParamSpec("max", 100, null, null, null, "Upper bound"),
    };

    public IBlock Create() => new ClampBlock();
}

public sealed class ClampBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return;
        var min = ctx.Param("min", 0);
        var max = ctx.Param("max", 100);
        if (min > max) (min, max) = (max, min);
        ctx.Emit(GenericBlockConventions.ValueOut, Math.Clamp(x.Value, min, max));
    }
}

// ── Deadband ────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Passes the input but suppresses changes smaller than <c>epsilon</c> (roadmap Epic 2Q): the output only
/// moves once the input has moved at least epsilon from the last emitted value — quiets a jittery sensor and
/// avoids re-commanding an actuator over noise.
/// </summary>
public sealed class DeadbandType : IBlockType
{
    public string TypeId => "deadband";
    public string Category => BlockCategories.Filter;
    public string Title => "Deadband";
    public string Description => "Holds the output until the input changes by at least epsilon.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Input value"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number(GenericBlockConventions.ValueOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("epsilon", 0.5, null, 0, null, "Minimum change to pass through"),
    };

    public IBlock Create() => new DeadbandBlock();
}

public sealed class DeadbandBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return;

        var eps = Math.Max(0, ctx.Param("epsilon", 0.5));
        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("last", x.Value);
            ctx.Emit(GenericBlockConventions.ValueOut, x.Value);
            return;
        }

        var last = ctx.GetState<double>("last");
        if (Math.Abs(x.Value - last) >= eps) last = x.Value;
        ctx.SetState("last", last);
        ctx.Emit(GenericBlockConventions.ValueOut, last);
    }
}

// ── Rate limiter (slew) ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Limits how fast the output can move toward the input (roadmap Epic 2Q): at most <c>maxRatePerSec</c> units
/// per second, time-aware via the actual tick gap. Soft-starts an actuator and protects it from step commands.
/// </summary>
public sealed class RateLimiterType : IBlockType
{
    public string TypeId => "rate_limiter";
    public string Category => BlockCategories.Filter;
    public string Title => "Rate limiter (slew)";
    public string Description => "Caps the output's rate of change toward the input.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Target value"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number(GenericBlockConventions.ValueOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("maxRatePerSec", 1, "/s", 0, null, "Maximum change per second"),
    };

    public IBlock Create() => new RateLimiterBlock();
}

public sealed class RateLimiterBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var target = ctx.ReadNumber("in");
        if (target is null) return;

        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("y", target.Value);
            ctx.SetState("t", ctx.Now);
            ctx.Emit(GenericBlockConventions.ValueOut, target.Value);
            return;
        }

        var y = ctx.GetState<double>("y");
        var last = ctx.GetState<DateTimeOffset>("t");
        var dt = Math.Max(0, (ctx.Now - last).TotalSeconds);
        var maxStep = Math.Max(0, ctx.Param("maxRatePerSec", 1)) * dt;

        var delta = Math.Clamp(target.Value - y, -maxStep, maxStep);
        y += delta;

        ctx.SetState("y", y);
        ctx.SetState("t", ctx.Now);
        ctx.Emit(GenericBlockConventions.ValueOut, Math.Round(y, 4));
    }
}

// ── Aggregate ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reduces up to four numeric inputs to one (roadmap Epic 2Q): min/max/sum/avg. Unbound inputs are ignored.
/// Take the hottest of several room sensors (max), the total power draw (sum), an average temperature (avg).
/// </summary>
public sealed class AggregateType : IBlockType
{
    public string TypeId => "aggregate";
    public string Category => BlockCategories.Math;
    public string Title => "Aggregate";
    public string Description => "Combines several numeric inputs with min/max/sum/avg.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in1", CapabilityKind.Number, "First input"),
        new BlockPortSpec("in2", CapabilityKind.Number, "Second input", Optional: true),
        new BlockPortSpec("in3", CapabilityKind.Number, "Third input", Optional: true),
        new BlockPortSpec("in4", CapabilityKind.Number, "Fourth input", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number(GenericBlockConventions.ValueOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("op", BlockOptionKind.Enum, "avg", "Aggregation", new[] { "min", "max", "sum", "avg" }),
    };

    public IBlock Create() => new AggregateBlock();
}

public sealed class AggregateBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var values = new[] { "in1", "in2", "in3", "in4" }
            .Select(ctx.ReadNumber)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();
        if (values.Count == 0) return;

        var op = (ctx.Option("op") ?? "avg").ToLowerInvariant();
        double result = op switch
        {
            "min" => values.Min(),
            "max" => values.Max(),
            "sum" => values.Sum(),
            _ => values.Average(),
        };
        ctx.Emit(GenericBlockConventions.ValueOut, Math.Round(result, 4));
    }
}

// ── Minimum dwell (anti-chatter) ────────────────────────────────────────────────────────────────

/// <summary>
/// Debounces a boolean by enforcing a minimum time between changes (roadmap Epic 2Q): once <c>state</c>
/// flips it is held for at least <c>dwellSec</c> before it may flip back. Protects relays/compressors from
/// short-cycling — the anti-chatter previously inlined in the ML toggle governor.
/// </summary>
public sealed class MinDwellType : IBlockType
{
    public string TypeId => "min_dwell";
    public string Category => BlockCategories.Time;
    public string Title => "Minimum dwell (anti-chatter)";
    public string Description => "Holds a boolean for a minimum time before it may change again.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Boolean, "Desired state"),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Boolean(GenericBlockConventions.StateOut, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("dwellSec", 300, "s", 0, null, "Minimum time between changes"),
    };

    public IBlock Create() => new MinDwellBlock();
}

public sealed class MinDwellBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var desired = GenericBlockConventions.ReadBool(ctx, "in");
        if (desired is null) return;

        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("out", desired.Value);
            ctx.SetState("changedAt", ctx.Now);
            ctx.Emit(GenericBlockConventions.StateOut, desired.Value);
            return;
        }

        var current = ctx.GetState<bool>("out");
        if (desired.Value != current)
        {
            var changedAt = ctx.GetState<DateTimeOffset>("changedAt");
            var dwell = Math.Max(0, ctx.Param("dwellSec", 300));
            if ((ctx.Now - changedAt).TotalSeconds >= dwell)
            {
                current = desired.Value;
                ctx.SetState("out", current);
                ctx.SetState("changedAt", ctx.Now);
            }
        }

        ctx.Emit(GenericBlockConventions.StateOut, current);
    }
}
