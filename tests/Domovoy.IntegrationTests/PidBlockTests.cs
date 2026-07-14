// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the PID controller block (roadmap Epic 2Q): proportional response, output clamping,
/// back-calculation anti-windup, feedforward, and the tuning presets. Pure — no infrastructure.
/// </summary>
public sealed class PidBlockTests
{
    private const string Out = "value";

    private static FakeBlockCtx Manual(double kp, double ki, double kd, double setpoint) => new()
    {
        Options = { ["preset"] = "manual" },
        Params = { ["kp"] = kp, ["ki"] = ki, ["kd"] = kd, ["setpoint"] = setpoint, ["outMin"] = 0, ["outMax"] = 100 },
    };

    [Fact]
    public void Pid_ProportionalDrivesTowardSetpoint_AndClamps()
    {
        var ctx = Manual(kp: 10, ki: 0, kd: 0, setpoint: 20);
        ctx.Inputs["pv"] = 10.0; // 10 below → error 10 → 10×10 = 100, clamped
        new PidBlock().Tick(ctx);
        Assert.Equal(100.0, ctx.GetNumber(Out));

        ctx.Inputs["pv"] = 25.0; // above setpoint → negative → clamped to 0
        new PidBlock().Tick(ctx); // fresh block: first-tick proportional-only path
        Assert.Equal(0.0, ctx.GetNumber(Out));
    }

    [Fact]
    public void Pid_AntiWindup_RecoversImmediatelyWhenErrorClears()
    {
        // kp=1, ki=1: while saturated at 100 the integral must NOT wind up, so when pv reaches the setpoint
        // the output drops at once (a wound-up integral would pin it high for many ticks).
        var block = new PidBlock();
        var ctx = Manual(kp: 1, ki: 1, kd: 0, setpoint: 100);
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        ctx.Now = t0; ctx.Inputs["pv"] = 0.0; block.Tick(ctx);         // seed, saturated 100
        for (var i = 1; i <= 5; i++)                                    // sustained saturation
        {
            ctx.Now = t0.AddSeconds(10 * i);
            block.Tick(ctx);
            Assert.Equal(100.0, ctx.GetNumber(Out));
        }

        ctx.Now = t0.AddSeconds(60); ctx.Inputs["pv"] = 100.0;         // error clears
        block.Tick(ctx);
        Assert.True(ctx.GetNumber(Out) <= 1.0, $"expected ~0 after windup-free saturation, got {ctx.GetNumber(Out)}");
    }

    [Fact]
    public void Pid_Feedforward_AddsAheadOfFeedback()
    {
        // No feedback gains, pv at setpoint (error 0) → output is purely the feedforward term ffGain×ff.
        var ctx = new FakeBlockCtx
        {
            Options = { ["preset"] = "manual" },
            Params = { ["kp"] = 0, ["ki"] = 0, ["kd"] = 0, ["setpoint"] = 20, ["ffGain"] = 2, ["outMin"] = 0, ["outMax"] = 100 },
            Inputs = { ["pv"] = 20.0, ["ff"] = 10.0 },
        };
        new PidBlock().Tick(ctx);
        Assert.Equal(20.0, ctx.GetNumber(Out)); // 2 × 10
    }

    [Fact]
    public void Pid_Preset_OverridesManualGains()
    {
        // 'responsive' has a large kp → a big error saturates the output even though the kp param is tiny.
        var ctx = new FakeBlockCtx
        {
            Options = { ["preset"] = "responsive" },
            Params = { ["kp"] = 0.01, ["ki"] = 0, ["kd"] = 0, ["setpoint"] = 30, ["outMin"] = 0, ["outMax"] = 100 },
            Inputs = { ["pv"] = 10.0 },
        };
        new PidBlock().Tick(ctx);
        Assert.Equal(100.0, ctx.GetNumber(Out)); // preset kp (10) × error (20) = 200 → clamped
    }
}
