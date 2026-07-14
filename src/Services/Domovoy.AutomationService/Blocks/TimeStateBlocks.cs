// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks;

// Generalized time/state primitives (roadmap Epic 2Q, Phase 2). Stateful building blocks — delays, pulses,
// edges, latches, counters and windowed filters — that the domain blocks used to re-implement inline
// (irrigation's interval timer, the ML governor's dwell). All are time-aware via the actual tick gap and
// persist their state across restarts once the block-state store is wired (Phase 2). Output convention as in
// GenericBlocks: numeric → "value", boolean → "state".

using static Domovoy.AutomationService.Blocks.GenericBlockConventions;

// ── On-delay (TON) ──────────────────────────────────────────────────────────────────────────────

/// <summary>Output turns true only after the input has stayed true continuously for <c>delaySec</c> (roadmap Epic 2Q).</summary>
public sealed class OnDelayType : IBlockType
{
    public string TypeId => "on_delay";
    public string Category => BlockCategories.Time;
    public string Title => "On-delay (TON)";
    public string Description => "Passes true only after the input has been true continuously for a delay.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("in", CapabilityKind.Boolean, "Input") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("delaySec", 60, "s", 0, null, "Delay before the output turns on"),
    };

    public IBlock Create() => new OnDelayBlock();
}

public sealed class OnDelayBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var input = ReadBool(ctx, "in");
        if (input is null) return;

        if (!input.Value)
        {
            ctx.SetState("tracking", false);
            ctx.Emit(StateOut, false);
            return;
        }

        if (!ctx.GetState<bool>("tracking"))
        {
            ctx.SetState("tracking", true);
            ctx.SetState("since", ctx.Now);
        }

        var since = ctx.GetState<DateTimeOffset>("since");
        var elapsed = (ctx.Now - since).TotalSeconds;
        ctx.Emit(StateOut, elapsed >= Math.Max(0, ctx.Param("delaySec", 60)));
    }
}

// ── Off-delay (TOFF) ─────────────────────────────────────────────────────────────────────────────

/// <summary>Output stays true for <c>delaySec</c> after the input goes false (roadmap Epic 2Q) — a stretch/hold timer.</summary>
public sealed class OffDelayType : IBlockType
{
    public string TypeId => "off_delay";
    public string Category => BlockCategories.Time;
    public string Title => "Off-delay (TOFF)";
    public string Description => "Holds the output true for a delay after the input goes false.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("in", CapabilityKind.Boolean, "Input") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("delaySec", 60, "s", 0, null, "How long the output stays on after the input drops"),
    };

    public IBlock Create() => new OffDelayBlock();
}

public sealed class OffDelayBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var input = ReadBool(ctx, "in");
        if (input is null) return;

        if (input.Value)
        {
            ctx.SetState("dropping", false);
            ctx.SetState("wasOn", true);
            ctx.Emit(StateOut, true);
            return;
        }

        if (!ctx.GetState<bool>("dropping"))
        {
            // Only stretch if it was on; if we've never been on, stay off.
            if (!ctx.GetState<bool>("wasOn")) { ctx.Emit(StateOut, false); return; }
            ctx.SetState("dropping", true);
            ctx.SetState("droppedAt", ctx.Now);
        }

        var droppedAt = ctx.GetState<DateTimeOffset>("droppedAt");
        var stillOn = (ctx.Now - droppedAt).TotalSeconds < Math.Max(0, ctx.Param("delaySec", 60));
        if (!stillOn) ctx.SetState("wasOn", false);
        ctx.Emit(StateOut, stillOn);
    }
}

// Note: off_delay tracks "wasOn" so it doesn't stretch a never-started signal. Set it whenever input is true.

// ── Pulse (one-shot) ─────────────────────────────────────────────────────────────────────────────

/// <summary>On a rising edge of the trigger, output true for <c>widthSec</c> then false (roadmap Epic 2Q).</summary>
public sealed class PulseType : IBlockType
{
    public string TypeId => "pulse";
    public string Category => BlockCategories.Time;
    public string Title => "Pulse (one-shot)";
    public string Description => "Emits a fixed-width true pulse on each rising edge of the trigger.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("trigger", CapabilityKind.Boolean, "Trigger") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("widthSec", 30, "s", 0, null, "Pulse width"),
    };

    public IBlock Create() => new PulseBlock();
}

public sealed class PulseBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var trig = ReadBool(ctx, "trigger") ?? false;
        var prev = ctx.GetState<bool>("prev");
        ctx.SetState("prev", trig);

        if (trig && !prev) // rising edge
        {
            ctx.SetState("until", ctx.Now.AddSeconds(Math.Max(0, ctx.Param("widthSec", 30))));
            ctx.SetState("armed", true);
        }

        var active = ctx.GetState<bool>("armed") && ctx.Now < ctx.GetState<DateTimeOffset>("until");
        if (!active) ctx.SetState("armed", false);
        ctx.Emit(StateOut, active);
    }
}

// ── Interval (periodic pulse) ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Periodic pulse (roadmap Epic 2Q): true for the first <c>widthSec</c> of every <c>periodSec</c>. An optional
/// <c>enable</c> input gates it. The reusable core of the irrigation sequencer's timing.
/// </summary>
public sealed class IntervalType : IBlockType
{
    public string TypeId => "interval";
    public string Category => BlockCategories.Time;
    public string Title => "Interval timer";
    public string Description => "True for a run-window at the start of every period (optionally gated by enable).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("enable", CapabilityKind.Boolean, "Optional gate — false suppresses the pulse", Optional: true),
    };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("periodSec", 3600, "s", 1, null, "Cycle length"),
        new BlockParamSpec("widthSec", 60, "s", 0, null, "How long the output is on each cycle"),
    };

    public IBlock Create() => new IntervalBlock();
}

public sealed class IntervalBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var enable = ReadBool(ctx, "enable");
        if (enable is false) { ctx.Emit(StateOut, false); return; }

        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("start", ctx.Now);
        }

        var start = ctx.GetState<DateTimeOffset>("start");
        var period = Math.Max(1, ctx.Param("periodSec", 3600));
        var width = Math.Max(0, ctx.Param("widthSec", 60));
        var phase = (ctx.Now - start).TotalSeconds % period;
        if (phase < 0) phase += period;
        ctx.Emit(StateOut, phase < width);
    }
}

// ── Edge detector ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Emits a one-tick true on a rising/falling/both edge of a boolean input (roadmap Epic 2Q).</summary>
public sealed class EdgeType : IBlockType
{
    public string TypeId => "edge";
    public string Category => BlockCategories.Time;
    public string Title => "Edge detector";
    public string Description => "Momentary true when the input changes (rising/falling/both).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("in", CapabilityKind.Boolean, "Input") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();
    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("type", BlockOptionKind.Enum, "rising", "Which edge to detect", new[] { "rising", "falling", "both" }),
    };

    public IBlock Create() => new EdgeBlock();
}

public sealed class EdgeBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var cur = ReadBool(ctx, "in");
        if (cur is null) return;

        var edge = false;
        if (ctx.GetState<bool>("seeded"))
        {
            var prev = ctx.GetState<bool>("prev");
            var type = (ctx.Option("type") ?? "rising").ToLowerInvariant();
            edge = type switch
            {
                "falling" => prev && !cur.Value,
                "both" => prev != cur.Value,
                _ => !prev && cur.Value, // rising
            };
        }
        ctx.SetState("seeded", true);
        ctx.SetState("prev", cur.Value);
        ctx.Emit(StateOut, edge);
    }
}

// ── Latch (SR flip-flop) ─────────────────────────────────────────────────────────────────────────

/// <summary>Set/reset latch that holds its state (roadmap Epic 2Q); <c>priority</c> resolves a simultaneous set+reset.</summary>
public sealed class LatchType : IBlockType
{
    public string TypeId => "latch";
    public string Category => BlockCategories.Time;
    public string Title => "Latch (SR)";
    public string Description => "Holds true after set, false after reset; priority breaks a tie.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("set", CapabilityKind.Boolean, "Set (→ true)"),
        new BlockPortSpec("reset", CapabilityKind.Boolean, "Reset (→ false)"),
    };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();
    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("priority", BlockOptionKind.Enum, "reset", "Winner when set and reset are both true", new[] { "set", "reset" }),
    };

    public IBlock Create() => new LatchBlock();
}

public sealed class LatchBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var set = ReadBool(ctx, "set") ?? false;
        var reset = ReadBool(ctx, "reset") ?? false;
        var q = ctx.GetState<bool>("q");

        if (set && reset) q = (ctx.Option("priority") ?? "reset").ToLowerInvariant() == "set";
        else if (set) q = true;
        else if (reset) q = false;

        ctx.SetState("q", q);
        ctx.Emit(StateOut, q);
    }
}

// ── Counter ──────────────────────────────────────────────────────────────────────────────────────

/// <summary>Counts rising edges of <c>inc</c>; a truthy <c>reset</c> zeroes it (roadmap Epic 2Q).</summary>
public sealed class CounterType : IBlockType
{
    public string TypeId => "counter";
    public string Category => BlockCategories.Time;
    public string Title => "Counter";
    public string Description => "Counts rising edges of the increment input; reset zeroes the count.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("inc", CapabilityKind.Boolean, "Increment on rising edge"),
        new BlockPortSpec("reset", CapabilityKind.Boolean, "Reset to zero while true", Optional: true),
    };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Number(ValueOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IBlock Create() => new CounterBlock();
}

public sealed class CounterBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var count = ctx.GetState<double>("count");
        var inc = ReadBool(ctx, "inc") ?? false;
        var prev = ctx.GetState<bool>("prev");
        ctx.SetState("prev", inc);

        if (inc && !prev) count++;
        if (ReadBool(ctx, "reset") == true) count = 0;

        ctx.SetState("count", count);
        ctx.Emit(ValueOut, count);
    }
}

// ── Sample & hold ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Latches the numeric input's value on each rising edge of the trigger, holding it between (roadmap Epic 2Q).</summary>
public sealed class SampleHoldType : IBlockType
{
    public string TypeId => "sample_hold";
    public string Category => BlockCategories.Filter;
    public string Title => "Sample & hold";
    public string Description => "Captures the input value on a trigger edge and holds it until the next.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("in", CapabilityKind.Number, "Value to sample"),
        new BlockPortSpec("trigger", CapabilityKind.Boolean, "Sample on rising edge"),
    };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Number(ValueOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IBlock Create() => new SampleHoldBlock();
}

public sealed class SampleHoldBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var trig = ReadBool(ctx, "trigger") ?? false;
        var prev = ctx.GetState<bool>("prev");
        ctx.SetState("prev", trig);

        if (trig && !prev)
        {
            var v = ctx.ReadNumber("in");
            if (v is not null) { ctx.SetState("held", v.Value); ctx.SetState("has", true); }
        }

        if (ctx.GetState<bool>("has")) ctx.Emit(ValueOut, ctx.GetState<double>("held"));
    }
}

// ── Debounce (boolean stability) ──────────────────────────────────────────────────────────────────

/// <summary>Passes a boolean only after it has held its new value for <c>stableSec</c> (roadmap Epic 2Q) — glitch filter.</summary>
public sealed class DebounceType : IBlockType
{
    public string TypeId => "debounce";
    public string Category => BlockCategories.Filter;
    public string Title => "Debounce";
    public string Description => "Ignores changes shorter than a stability window.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("in", CapabilityKind.Boolean, "Input") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Boolean(StateOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("stableSec", 5, "s", 0, null, "Required stable time before a change passes"),
    };

    public IBlock Create() => new DebounceBlock();
}

public sealed class DebounceBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var input = ReadBool(ctx, "in");
        if (input is null) return;

        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("out", input.Value);
            ctx.SetState("cand", input.Value);
            ctx.SetState("since", ctx.Now);
            ctx.Emit(StateOut, input.Value);
            return;
        }

        var outv = ctx.GetState<bool>("out");
        if (input.Value != ctx.GetState<bool>("cand"))
        {
            ctx.SetState("cand", input.Value);
            ctx.SetState("since", ctx.Now);
        }
        else if (input.Value != outv)
        {
            var since = ctx.GetState<DateTimeOffset>("since");
            if ((ctx.Now - since).TotalSeconds >= Math.Max(0, ctx.Param("stableSec", 5)))
            {
                outv = input.Value;
                ctx.SetState("out", outv);
            }
        }
        ctx.Emit(StateOut, outv);
    }
}

// ── Moving average (windowed) ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Simple moving average over the last <c>window</c> samples (roadmap Epic 2Q). Unlike the EMA it weights all
/// samples in the window equally. Window state resets cold on restart (best-effort persistence).
/// </summary>
public sealed class MovingAverageType : IBlockType
{
    public string TypeId => "moving_average";
    public string Category => BlockCategories.Filter;
    public string Title => "Moving average";
    public string Description => "Average of the last N samples of a numeric signal.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("in", CapabilityKind.Number, "Signal") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Number(ValueOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("window", 5, null, 1, 240, "Number of samples to average"),
    };

    public IBlock Create() => new WindowedBlock(median: false);
}

// ── Median filter (windowed) ──────────────────────────────────────────────────────────────────────

/// <summary>Median over the last <c>window</c> samples (roadmap Epic 2Q) — rejects spikes an average would smear.</summary>
public sealed class MedianFilterType : IBlockType
{
    public string TypeId => "median_filter";
    public string Category => BlockCategories.Filter;
    public string Title => "Median filter";
    public string Description => "Median of the last N samples — robust to outliers/spikes.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("in", CapabilityKind.Number, "Signal") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Number(ValueOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("window", 5, null, 1, 240, "Number of samples in the window"),
    };

    public IBlock Create() => new WindowedBlock(median: true);
}

// ── Ramp (soft transition to a target) ────────────────────────────────────────────────────────

/// <summary>
/// Ramps the output from a starting value toward a target at a fixed rate (roadmap Epic 2Q) — a soft-start.
/// Unlike <c>rate_limiter</c> (which seeds to its first input), <c>ramp</c> begins at <c>start</c> and climbs
/// toward the <c>target</c> input, so an actuator eases on from a known baseline.
/// </summary>
public sealed class RampType : IBlockType
{
    public string TypeId => "ramp";
    public string Category => BlockCategories.Control;
    public string Title => "Ramp";
    public string Description => "Moves from a start value toward a target at a fixed rate per second.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[] { new BlockPortSpec("target", CapabilityKind.Number, "Target value") };
    public IReadOnlyList<Capability> Outputs { get; } = new[] { WellKnownCapabilities.Number(ValueOut, writable: false) };
    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("ratePerSec", 1, "/s", 0, null, "Change per second toward the target"),
        new BlockParamSpec("start", 0, null, null, null, "Initial output value"),
    };

    public IBlock Create() => new RampBlock();
}

public sealed class RampBlock : IBlock
{
    public void Tick(IBlockContext ctx)
    {
        var target = ctx.ReadNumber("target");
        if (target is null) return;

        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("y", ctx.Param("start", 0));
            ctx.SetState("t", ctx.Now);
            ctx.Emit(ValueOut, ctx.Param("start", 0));
            return;
        }

        var y = ctx.GetState<double>("y");
        var last = ctx.GetState<DateTimeOffset>("t");
        var dt = Math.Max(0, (ctx.Now - last).TotalSeconds);
        var maxStep = Math.Max(0, ctx.Param("ratePerSec", 1)) * dt;
        y += Math.Clamp(target.Value - y, -maxStep, maxStep);

        ctx.SetState("y", y);
        ctx.SetState("t", ctx.Now);
        ctx.Emit(ValueOut, Math.Round(y, 4));
    }
}

/// <summary>Shared ring-buffer body for the count-windowed filters (moving_average / median_filter).</summary>
public sealed class WindowedBlock : IBlock
{
    private readonly bool _median;
    public WindowedBlock(bool median) => _median = median;

    public void Tick(IBlockContext ctx)
    {
        var x = ctx.ReadNumber("in");
        if (x is null) return;

        var n = (int)Math.Max(1, Math.Round(ctx.Param("window", 5)));
        var buf = ctx.GetState<List<double>>("buf") ?? new List<double>();
        buf.Add(x.Value);
        while (buf.Count > n) buf.RemoveAt(0);
        ctx.SetState("buf", buf);

        double result;
        if (_median)
        {
            var sorted = buf.OrderBy(v => v).ToList();
            var mid = sorted.Count / 2;
            result = sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }
        else result = buf.Average();

        ctx.Emit(ValueOut, Math.Round(result, 4));
    }
}
