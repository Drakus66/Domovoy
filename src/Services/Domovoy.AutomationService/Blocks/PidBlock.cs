// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// PID controller (roadmap Epic 2Q) — the foundation for controlling devices by <b>power/position</b>
/// (0–100 %: dimmer, valve, VFD, PWM heater) rather than bang-bang on/off, which is the basis of energy
/// saving and lets ML propose a <i>setpoint</i> that PID tracks smoothly.
///
/// <para><b>Primary loop:</b> tracks <c>sp</c> (or the <c>setpoint</c> param when <c>sp</c> is unbound) with the
/// measured <c>pv</c>. Derivative acts on the measurement (not the error) so a setpoint change causes no
/// derivative kick. Integral uses back-calculation anti-windup: while the output is saturated the integral is
/// pulled back so it can't wind up. The output is always clamped to <c>[outMin, outMax]</c>. Time-aware via the
/// real tick gap. A missing <c>pv</c> holds (emits nothing) — never commands power on bad data.</para>
///
/// <para><b>Feedforward (<c>ff</c>, optional second input):</b> a disturbance measurement (classically outdoor
/// temperature) added ahead of the feedback: <c>out = clamp(PID(sp−pv) + ffGain·ff)</c>. Removes lag — react to
/// the disturbance before the room drifts. Weather-compensation curves, cascade and multi-input are built by
/// composition (<c>linear_map</c>/<c>expression → pid</c>), not more ports.</para>
///
/// <para><b>Presets</b> hide raw kp/ki/kd from non-expert users: pick a behaviour profile (gentle / balanced /
/// responsive / eco) and the gains are filled in; <c>manual</c> uses the params. Preset numbers here are generic
/// starting points — a template scopes them to its application (slow floor heating vs a fast valve).</para>
/// </summary>
public sealed class PidType : IBlockType
{
    public string TypeId => "pid";
    public string Category => BlockCategories.Control;
    public string Title => "PID controller";
    public string Description => "Continuous 0–100 % control of a device by power/position, with optional feedforward.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("pv", CapabilityKind.Number, "Process value (measured, e.g. temperature)"),
        new BlockPortSpec("sp", CapabilityKind.Number, "Setpoint (overrides the setpoint param when bound)", Optional: true),
        new BlockPortSpec("ff", CapabilityKind.Number, "Optional feedforward / disturbance (e.g. outdoor temp)", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number("value", "%", 0, 100, step: 1, writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("setpoint", 21, null, null, null, "Target (used when the sp input is unbound)"),
        new BlockParamSpec("kp", 5, null, 0, null, "Proportional gain (manual preset)"),
        new BlockParamSpec("ki", 0.1, null, 0, null, "Integral gain (manual preset)"),
        new BlockParamSpec("kd", 1, null, 0, null, "Derivative gain (manual preset)"),
        new BlockParamSpec("ffGain", 0, null, null, null, "Feedforward coefficient (0 = feedforward off)"),
        new BlockParamSpec("outMin", 0, "%", null, null, "Minimum output"),
        new BlockParamSpec("outMax", 100, "%", null, null, "Maximum output"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("preset", BlockOptionKind.Enum, "balanced",
            "Tuning profile — gentle (smooth, slow), balanced, responsive (fast, may overshoot), eco (energy-biased), or manual (use kp/ki/kd)",
            new[] { "manual", "gentle", "balanced", "responsive", "eco" }),
    };

    public IBlock Create() => new PidBlock();
}

public sealed class PidBlock : IBlock
{
    /// <summary>Generic starting-point gains per behaviour profile (a template calibrates these per application).</summary>
    private static (double kp, double ki, double kd)? Preset(string preset) => preset switch
    {
        "gentle" => (2, 0.05, 0),
        "balanced" => (5, 0.1, 1),
        "responsive" => (10, 0.2, 5),
        "eco" => (3, 0.03, 0),
        _ => null, // manual / unknown → use params
    };

    public void Tick(IBlockContext ctx)
    {
        var pv = ctx.ReadNumber("pv");
        if (pv is null) return; // hold on bad data — never command power blindly

        var sp = ctx.ReadNumber("sp") ?? ctx.Param("setpoint", 21);

        var preset = (ctx.Option("preset") ?? "balanced").ToLowerInvariant();
        var (kp, ki, kd) = Preset(preset)
            ?? (ctx.Param("kp", 5), ctx.Param("ki", 0.1), ctx.Param("kd", 1));

        var outMin = ctx.Param("outMin", 0);
        var outMax = ctx.Param("outMax", 100);
        if (outMin > outMax) (outMin, outMax) = (outMax, outMin);

        var ffGain = ctx.Param("ffGain", 0);
        var ffTerm = ffGain != 0 ? ffGain * (ctx.ReadNumber("ff") ?? 0) : 0;

        var error = sp - pv.Value;

        // First tick: seed, output proportional + feedforward only (no dt for I/D yet).
        if (!ctx.GetState<bool>("seeded"))
        {
            ctx.SetState("seeded", true);
            ctx.SetState("pv", pv.Value);
            ctx.SetState("i", 0.0);
            ctx.SetState("t", ctx.Now);
            ctx.Emit("value", Math.Round(Math.Clamp(kp * error + ffTerm, outMin, outMax), 2));
            return;
        }

        var last = ctx.GetState<DateTimeOffset>("t");
        var dt = (ctx.Now - last).TotalSeconds;
        var integral = ctx.GetState<double>("i");
        var prevPv = ctx.GetState<double>("pv");

        double dTerm = 0;
        if (dt > 0)
        {
            integral += error * dt;                    // tentative integration
            dTerm = -kd * (pv.Value - prevPv) / dt;    // derivative on measurement (no setpoint kick)
        }

        var raw = kp * error + ki * integral + dTerm + ffTerm;
        var output = Math.Clamp(raw, outMin, outMax);

        // Back-calculation anti-windup: if saturated, pull the integral back so it can't accumulate.
        if (ki != 0 && raw != output)
            integral -= (raw - output) / ki;

        ctx.SetState("i", integral);
        ctx.SetState("pv", pv.Value);
        ctx.SetState("t", ctx.Now);
        ctx.Emit("value", Math.Round(output, 2));
    }
}
