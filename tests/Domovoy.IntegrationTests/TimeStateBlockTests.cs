// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the time/state primitives (roadmap Epic 2Q, Phase 2): on/off delay, pulse, interval, edge,
/// latch, counter, sample_hold, debounce and the windowed filters. Pure — time is driven by the fake clock.
/// </summary>
public sealed class TimeStateBlockTests
{
    private const string StateOut = "state";
    private const string ValueOut = "value";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnDelay_TurnsOnAfterContinuousTrue()
    {
        var block = new OnDelayBlock();
        var ctx = new FakeBlockCtx { Params = { ["delaySec"] = 100 }, Now = T0, Inputs = { ["in"] = true } };
        block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                 // just went true

        ctx.Now = T0.AddSeconds(50); block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                 // not long enough

        ctx.Now = T0.AddSeconds(150); block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                  // elapsed ≥ delay

        ctx.Inputs["in"] = false; block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                 // input dropped → off, timer resets
    }

    [Fact]
    public void OffDelay_HoldsAfterInputDrops()
    {
        var block = new OffDelayBlock();
        var ctx = new FakeBlockCtx { Params = { ["delaySec"] = 100 }, Now = T0, Inputs = { ["in"] = true } };
        block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));

        ctx.Now = T0.AddSeconds(1); ctx.Inputs["in"] = false; block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                  // still on (stretching)

        ctx.Now = T0.AddSeconds(150); block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                 // stretch elapsed
    }

    [Fact]
    public void Pulse_FixedWidthOnRisingEdge()
    {
        var block = new PulseBlock();
        var ctx = new FakeBlockCtx { Params = { ["widthSec"] = 30 }, Now = T0, Inputs = { ["trigger"] = true } };
        block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                  // rising edge → pulse

        ctx.Now = T0.AddSeconds(40); block.Tick(ctx);        // trigger still held (no new edge)
        Assert.False(ctx.GetBool(StateOut));                 // width elapsed
    }

    [Fact]
    public void Interval_OnDuringRunWindow()
    {
        var block = new IntervalBlock();
        var ctx = new FakeBlockCtx { Params = { ["periodSec"] = 100, ["widthSec"] = 20 }, Now = T0 };
        block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                  // phase 0 < width

        ctx.Now = T0.AddSeconds(30); block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                 // past the window

        ctx.Now = T0.AddSeconds(100); block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                  // next cycle
    }

    [Fact]
    public void Interval_EnableGate()
    {
        var ctx = new FakeBlockCtx { Params = { ["periodSec"] = 100, ["widthSec"] = 20 }, Now = T0, Inputs = { ["enable"] = false } };
        new IntervalBlock().Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));
    }

    [Fact]
    public void Edge_Rising()
    {
        var block = new EdgeBlock();
        var ctx = new FakeBlockCtx { Inputs = { ["in"] = false } };
        block.Tick(ctx); Assert.False(ctx.GetBool(StateOut)); // seed
        ctx.Inputs["in"] = true; block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                    // rising edge
        block.Tick(ctx); Assert.False(ctx.GetBool(StateOut));  // held true → no edge
    }

    [Fact]
    public void Latch_SetResetHold()
    {
        var block = new LatchBlock();
        var ctx = new FakeBlockCtx { Inputs = { ["set"] = false, ["reset"] = false } };
        block.Tick(ctx); Assert.False(ctx.GetBool(StateOut));

        ctx.Inputs["set"] = true; block.Tick(ctx); Assert.True(ctx.GetBool(StateOut));
        ctx.Inputs["set"] = false; block.Tick(ctx); Assert.True(ctx.GetBool(StateOut)); // holds
        ctx.Inputs["reset"] = true; block.Tick(ctx); Assert.False(ctx.GetBool(StateOut));
    }

    [Fact]
    public void Latch_TieBreaksByPriority()
    {
        var ctx = new FakeBlockCtx { Options = { ["priority"] = "set" }, Inputs = { ["set"] = true, ["reset"] = true } };
        new LatchBlock().Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));
    }

    [Fact]
    public void Counter_CountsRisingEdges_AndResets()
    {
        var block = new CounterBlock();
        var ctx = new FakeBlockCtx { Inputs = { ["inc"] = false, ["reset"] = false } };
        block.Tick(ctx);
        ctx.Inputs["inc"] = true; block.Tick(ctx);   // edge → 1
        ctx.Inputs["inc"] = false; block.Tick(ctx);
        ctx.Inputs["inc"] = true; block.Tick(ctx);   // edge → 2
        Assert.Equal(2.0, ctx.GetNumber(ValueOut));

        ctx.Inputs["reset"] = true; block.Tick(ctx);
        Assert.Equal(0.0, ctx.GetNumber(ValueOut));
    }

    [Fact]
    public void SampleHold_CapturesOnTriggerEdge()
    {
        var block = new SampleHoldBlock();
        var ctx = new FakeBlockCtx { Inputs = { ["in"] = 10.0, ["trigger"] = false } };
        block.Tick(ctx);
        ctx.Inputs["trigger"] = true; block.Tick(ctx);        // sample → 10
        Assert.Equal(10.0, ctx.GetNumber(ValueOut));

        ctx.Inputs["in"] = 99.0; ctx.Inputs["trigger"] = true; block.Tick(ctx); // held (no new edge)
        Assert.Equal(10.0, ctx.GetNumber(ValueOut));

        ctx.Inputs["trigger"] = false; block.Tick(ctx);
        ctx.Inputs["trigger"] = true; block.Tick(ctx);        // new edge → 99
        Assert.Equal(99.0, ctx.GetNumber(ValueOut));
    }

    [Fact]
    public void Debounce_IgnoresShortGlitch()
    {
        var block = new DebounceBlock();
        var ctx = new FakeBlockCtx { Params = { ["stableSec"] = 10 }, Now = T0, Inputs = { ["in"] = false } };
        block.Tick(ctx); Assert.False(ctx.GetBool(StateOut));

        ctx.Now = T0.AddSeconds(1); ctx.Inputs["in"] = true; block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                  // change not yet stable

        ctx.Now = T0.AddSeconds(5); block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));                  // still within window

        ctx.Now = T0.AddSeconds(15); block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));                   // stable long enough → passes
    }

    [Fact]
    public void MovingAverage_AveragesWindow()
    {
        var block = new WindowedBlock(median: false);
        var ctx = new FakeBlockCtx { Params = { ["window"] = 3 } };
        foreach (var v in new[] { 1.0, 2.0, 3.0 }) { ctx.Inputs["in"] = v; block.Tick(ctx); }
        Assert.Equal(2.0, ctx.GetNumber(ValueOut));
    }

    [Fact]
    public void Ramp_MovesFromStartTowardTargetAtRate()
    {
        var block = new RampBlock();
        var ctx = new FakeBlockCtx { Params = { ["ratePerSec"] = 1, ["start"] = 0 }, Now = T0, Inputs = { ["target"] = 10.0 } };
        block.Tick(ctx);
        Assert.Equal(0.0, ctx.GetNumber(ValueOut));           // starts at 'start', not at the target

        ctx.Now = T0.AddSeconds(5); block.Tick(ctx);
        Assert.Equal(5.0, ctx.GetNumber(ValueOut));           // 1/s × 5s

        ctx.Now = T0.AddSeconds(100); block.Tick(ctx);
        Assert.Equal(10.0, ctx.GetNumber(ValueOut));          // reaches and holds the target
    }

    [Fact]
    public void MedianFilter_RejectsSpike()
    {
        var block = new WindowedBlock(median: true);
        var ctx = new FakeBlockCtx { Params = { ["window"] = 3 } };
        foreach (var v in new[] { 1.0, 100.0, 2.0 }) { ctx.Inputs["in"] = v; block.Tick(ctx); }
        Assert.Equal(2.0, ctx.GetNumber(ValueOut)); // median of {1,2,100} = 2, not the spike
    }
}
