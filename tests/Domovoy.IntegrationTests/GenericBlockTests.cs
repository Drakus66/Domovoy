// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the generalized primitive blocks (roadmap Epic 2Q): comparator, hysteresis, window, logic,
/// select, linear_map, clamp, deadband, rate_limiter, aggregate, min_dwell. Pure — no infrastructure.
/// </summary>
public sealed class GenericBlockTests
{
    private const string StateOut = "state";
    private const string ValueOut = "value";

    // ── Comparator ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("gt", 25, 20, true)]
    [InlineData("gt", 15, 20, false)]
    [InlineData("lte", 20, 20, true)]
    [InlineData("lt", 20, 20, false)]
    public void Comparator_AppliesOperator(string op, double input, double threshold, bool expected)
    {
        var ctx = new FakeBlockCtx { Params = { ["threshold"] = threshold }, Options = { ["op"] = op }, Inputs = { ["in"] = input } };
        new ComparatorBlock().Tick(ctx);
        Assert.Equal(expected, ctx.Get(StateOut));
    }

    [Fact]
    public void Comparator_LiveThresholdInput_OverridesParam()
    {
        var ctx = new FakeBlockCtx { Params = { ["threshold"] = 0 }, Options = { ["op"] = "gt" }, Inputs = { ["in"] = 5.0, ["threshold"] = 10.0 } };
        new ComparatorBlock().Tick(ctx);
        Assert.Equal(false, ctx.Get(StateOut)); // 5 > 10 is false → the bound threshold won, not the param 0
    }

    // ── Hysteresis (the generalized bang-bang) ────────────────────────────────────────────────────

    [Fact]
    public void Hysteresis_OnAboveHigh_HoldsInBand_OffBelowLow()
    {
        var block = new HysteresisBlock();
        var ctx = new FakeBlockCtx { Params = { ["high"] = 22, ["low"] = 20 } };

        ctx.Inputs["in"] = 23.0; block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));  // above high → on

        ctx.Inputs["in"] = 21.0; block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));  // inside the dead-band → holds on

        ctx.Inputs["in"] = 19.0; block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut)); // below low → off

        ctx.Inputs["in"] = 21.0; block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut)); // dead-band → holds off
    }

    [Fact]
    public void Hysteresis_Inverted_IsCoolingSense()
    {
        var block = new HysteresisBlock();
        var ctx = new FakeBlockCtx { Params = { ["high"] = 26, ["low"] = 24 }, Options = { ["invert"] = "true" } };

        ctx.Inputs["in"] = 27.0; block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut)); // hot → off (cooling would be a separate leg)

        ctx.Inputs["in"] = 23.0; block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));  // below low → on (inverted)
    }

    // ── Window ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(50, false, true)]
    [InlineData(150, false, false)]
    [InlineData(150, true, true)]
    public void Window_InRange(double input, bool invert, bool expected)
    {
        var ctx = new FakeBlockCtx { Params = { ["low"] = 0, ["high"] = 100 }, Options = { ["invert"] = invert ? "true" : "false" }, Inputs = { ["in"] = input } };
        new WindowBlock().Tick(ctx);
        Assert.Equal(expected, ctx.Get(StateOut));
    }

    // ── Logic ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("and", true, true, true)]
    [InlineData("and", true, false, false)]
    [InlineData("or", false, true, true)]
    [InlineData("xor", true, true, false)]
    [InlineData("nand", true, true, false)]
    public void Logic_Combines(string op, bool a, bool b, bool expected)
    {
        var ctx = new FakeBlockCtx { Options = { ["op"] = op }, Inputs = { ["a"] = a, ["b"] = b } };
        new LogicBlock().Tick(ctx);
        Assert.Equal(expected, ctx.Get(StateOut));
    }

    [Fact]
    public void Logic_IgnoresUnboundInputs()
    {
        // Only 'a' bound → AND of one input is that input.
        var ctx = new FakeBlockCtx { Options = { ["op"] = "and" }, Inputs = { ["a"] = true } };
        new LogicBlock().Tick(ctx);
        Assert.Equal(true, ctx.Get(StateOut));
    }

    // ── Select ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, 42)]
    [InlineData(false, 7)]
    public void Select_PicksBySelector(bool sel, double expected)
    {
        var ctx = new FakeBlockCtx { Inputs = { ["sel"] = sel, ["a"] = 42.0, ["b"] = 7.0 } };
        new SelectBlock().Tick(ctx);
        Assert.Equal(expected, ctx.GetNumber(ValueOut));
    }

    // ── Linear map / clamp ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void LinearMap_ScalesAndOffsets()
    {
        var ctx = new FakeBlockCtx { Params = { ["gain"] = 2, ["offset"] = 1 }, Inputs = { ["in"] = 10.0 } };
        new LinearMapBlock().Tick(ctx);
        Assert.Equal(21.0, ctx.GetNumber(ValueOut));
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    [InlineData(42, 42)]
    public void Clamp_Constrains(double input, double expected)
    {
        var ctx = new FakeBlockCtx { Params = { ["min"] = 0, ["max"] = 100 }, Inputs = { ["in"] = input } };
        new ClampBlock().Tick(ctx);
        Assert.Equal(expected, ctx.GetNumber(ValueOut));
    }

    // ── Deadband ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Deadband_SuppressesSmallChanges()
    {
        var block = new DeadbandBlock();
        var ctx = new FakeBlockCtx { Params = { ["epsilon"] = 1 } };

        ctx.Inputs["in"] = 10.0; block.Tick(ctx);
        Assert.Equal(10.0, ctx.GetNumber(ValueOut)); // seed

        ctx.Inputs["in"] = 10.5; block.Tick(ctx);
        Assert.Equal(10.0, ctx.GetNumber(ValueOut)); // change < epsilon → held

        ctx.Inputs["in"] = 11.5; block.Tick(ctx);
        Assert.Equal(11.5, ctx.GetNumber(ValueOut)); // change ≥ epsilon → passes
    }

    // ── Rate limiter ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RateLimiter_CapsRateOfChange()
    {
        var block = new RateLimiterBlock();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ctx = new FakeBlockCtx { Params = { ["maxRatePerSec"] = 1 }, Now = t0, Inputs = { ["in"] = 0.0 } };
        block.Tick(ctx);
        Assert.Equal(0.0, ctx.GetNumber(ValueOut)); // seed

        ctx.Now = t0.AddSeconds(10);
        ctx.Inputs["in"] = 100.0;
        block.Tick(ctx);
        Assert.Equal(10.0, ctx.GetNumber(ValueOut)); // 1/s × 10s = at most +10
    }

    // ── Aggregate ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("avg", 2)]
    [InlineData("max", 3)]
    [InlineData("min", 1)]
    [InlineData("sum", 4)]
    public void Aggregate_Reduces(string op, double expected)
    {
        var ctx = new FakeBlockCtx { Options = { ["op"] = op }, Inputs = { ["in1"] = 1.0, ["in2"] = 3.0 } };
        new AggregateBlock().Tick(ctx);
        Assert.Equal(expected, ctx.GetNumber(ValueOut));
    }

    // ── Min dwell ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MinDwell_HoldsUntilDwellElapsed()
    {
        var block = new MinDwellBlock();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ctx = new FakeBlockCtx { Params = { ["dwellSec"] = 300 }, Now = t0, Inputs = { ["in"] = false } };
        block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut)); // seed off

        ctx.Now = t0.AddSeconds(60);
        ctx.Inputs["in"] = true;
        block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut)); // wants on but dwell not elapsed → held off

        ctx.Now = t0.AddSeconds(400);
        block.Tick(ctx);
        Assert.True(ctx.GetBool(StateOut)); // dwell elapsed → allowed to change
    }
}
