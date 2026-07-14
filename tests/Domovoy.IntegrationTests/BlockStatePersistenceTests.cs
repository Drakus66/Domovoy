// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Tests for block-state persistence across restarts (roadmap Epic 2Q, Phase 2): the JSON round-trip +
/// <see cref="StateCoerce"/> preserve the CLR types blocks keep, and stateful blocks (counter, latch, on-delay)
/// resume from a restored snapshot instead of cold-starting.
/// </summary>
public sealed class BlockStatePersistenceTests
{
    private const string StateOut = "state";
    private const string ValueOut = "value";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Serialize a live state bag and read every value back at its original type.</summary>
    private static Dictionary<string, object?> RoundTrip(Dictionary<string, object?> state) =>
        BlockStateJson.Deserialize(BlockStateJson.Serialize(state));

    [Fact]
    public void RoundTrip_PreservesPrimitiveTypes()
    {
        var original = new Dictionary<string, object?>
        {
            ["flag"] = true,
            ["num"] = 42.5,
            ["text"] = "hello",
            ["ts"] = T0.AddHours(3),
            ["buf"] = new List<double> { 1, 2, 3 },
        };

        var restored = RoundTrip(original);

        Assert.True(StateCoerce.As<bool>(restored["flag"]));
        Assert.Equal(42.5, StateCoerce.As<double>(restored["num"]));
        Assert.Equal("hello", StateCoerce.As<string>(restored["text"]));
        Assert.Equal(T0.AddHours(3), StateCoerce.As<DateTimeOffset>(restored["ts"]));
        Assert.Equal(new List<double> { 1, 2, 3 }, StateCoerce.As<List<double>>(restored["buf"]));
    }

    [Fact]
    public void Counter_ResumesFromRestoredState()
    {
        var block = new CounterBlock();
        var ctx = new FakeBlockCtx { Inputs = { ["inc"] = false, ["reset"] = false } };
        block.Tick(ctx);
        ctx.Inputs["inc"] = true; block.Tick(ctx);   // → 1
        ctx.Inputs["inc"] = false; block.Tick(ctx);
        ctx.Inputs["inc"] = true; block.Tick(ctx);    // → 2
        Assert.Equal(2.0, ctx.GetNumber(ValueOut));

        // Simulate a restart: persist, then seed a fresh context+block from the restored snapshot.
        var restored = RoundTrip(ctx.State);
        var ctx2 = new FakeBlockCtx { Inputs = { ["inc"] = false, ["reset"] = false } };
        foreach (var kv in restored) ctx2.State[kv.Key] = kv.Value;

        var block2 = new CounterBlock();
        block2.Tick(ctx2);                             // inc low → prime a clean edge (count holds at 2)
        Assert.Equal(2.0, ctx2.GetNumber(ValueOut));   // resumed, not reset to 0
        ctx2.Inputs["inc"] = true; block2.Tick(ctx2);  // new rising edge → 3
        Assert.Equal(3.0, ctx2.GetNumber(ValueOut));
    }

    [Fact]
    public void Latch_ResumesSetStateAfterRestart()
    {
        var ctx = new FakeBlockCtx { Inputs = { ["set"] = true, ["reset"] = false } };
        new LatchBlock().Tick(ctx);
        Assert.True(ctx.GetBool(StateOut));

        var restored = RoundTrip(ctx.State);
        var ctx2 = new FakeBlockCtx { Inputs = { ["set"] = false, ["reset"] = false } };
        foreach (var kv in restored) ctx2.State[kv.Key] = kv.Value;

        new LatchBlock().Tick(ctx2); // no set/reset → must hold the restored true
        Assert.True(ctx2.GetBool(StateOut));
    }

    [Fact]
    public void OnDelay_ResumesTimerAcrossRestart()
    {
        // Start the timer, persist mid-count, restore, and confirm the elapsed time (a DateTimeOffset) survived
        // so it fires on schedule rather than mis-firing from a zeroed clock.
        var block = new OnDelayBlock();
        var ctx = new FakeBlockCtx { Params = { ["delaySec"] = 100 }, Now = T0, Inputs = { ["in"] = true } };
        block.Tick(ctx);
        Assert.False(ctx.GetBool(StateOut));

        var restored = RoundTrip(ctx.State);
        var ctx2 = new FakeBlockCtx { Params = { ["delaySec"] = 100 }, Now = T0.AddSeconds(150), Inputs = { ["in"] = true } };
        foreach (var kv in restored) ctx2.State[kv.Key] = kv.Value;

        new OnDelayBlock().Tick(ctx2);
        Assert.True(ctx2.GetBool(StateOut)); // 150s ≥ 100s from the restored start instant
    }
}
