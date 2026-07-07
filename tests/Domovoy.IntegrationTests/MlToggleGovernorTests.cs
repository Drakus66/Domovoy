// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Ml.Governors;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the ML toggle governor (roadmap Epic 2I, Phase 2): probability threshold, Shadow gating,
/// Bounded hysteresis, min-dwell anti-chatter, and disagreement-rate drift auto-demote. Pure — the predictor
/// (a probability) is a stub.
/// </summary>
public sealed class MlToggleGovernorTests
{
    private const string Out = CapabilityIds.OnOff;

    private static MlToggleGovernor Governor(Func<DateTimeOffset, double?> probability) =>
        new((now, _, _) => probability(now), measuredInput: "state", boundOutput: Out);

    [Fact]
    public void Shadow_EmitsProbability_ButNoBoundCommand()
    {
        var ctx = new FakeContext { Params = { ["stage"] = 0 } };
        Governor(_ => 0.9).Tick(ctx);

        Assert.Equal(0.9, ctx.Get(MlGovernorBase.ProposedSetpoint));
        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(Out)); // Shadow = no actuation
    }

    [Fact]
    public void Full_CommandsOn_WhenProbabilityAboveThreshold()
    {
        var ctx = Ctx(stage: 2);
        Governor(_ => 0.8).Tick(ctx);

        Assert.Equal(2d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.Equal(true, ctx.Get(Out));
    }

    [Fact]
    public void Full_CommandsOff_WhenProbabilityBelowThreshold()
    {
        var ctx = Ctx(stage: 2);
        Governor(_ => 0.2).Tick(ctx);
        Assert.Equal(false, ctx.Get(Out));
    }

    [Fact]
    public void Bounded_RequiresMargin_ToTurnOn()
    {
        // Bounded margin 0.2 → need prob >= 0.7 to flip on; 0.6 is within the hysteresis band → stays off.
        var ctx = Ctx(stage: 1, margin: 0.2);
        Governor(_ => 0.6).Tick(ctx);

        Assert.Equal(1d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.Equal(false, ctx.Get(Out));
    }

    [Fact]
    public void MinDwell_HoldsState_UntilDwellElapses()
    {
        var prob = 0.9;                       // mutated per tick
        var block = Governor(_ => prob);
        var t0 = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        var on = Ctx(stage: 2, now: t0);
        block.Tick(on);
        Assert.Equal(true, on.Get(Out));      // turns on

        prob = 0.1;
        var held = Ctx(stage: 2, now: t0.AddMinutes(5));
        block.Tick(held);
        Assert.Equal(true, held.Get(Out));    // 5 min < 10 min dwell → holds on

        var flipped = Ctx(stage: 2, now: t0.AddMinutes(15));
        block.Tick(flipped);
        Assert.Equal(false, flipped.Get(Out)); // 15 min ≥ dwell → flips off
    }

    [Fact]
    public void Drift_AutoDemotesToShadow_WhenPredictionsDisagreeWithReality()
    {
        // Predicts on (0.9) while reality is off (0) → disagreement rate 1.0 > threshold 0.5 → Shadow.
        var ctx = new FakeContext
        {
            Params = { ["stage"] = 2, ["probThreshold"] = 0.5, ["driftThreshold"] = 0.5, ["driftWindowMin"] = 60 },
            Inputs = { ["state"] = 0.0 },
        };
        Governor(_ => 0.9).Tick(ctx);

        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(Out));
    }

    // High drift threshold isolates the staging/threshold logic (drift has its own test).
    private static FakeContext Ctx(int stage, double margin = 0, DateTimeOffset? now = null)
    {
        var ctx = new FakeContext
        {
            Params =
            {
                ["stage"] = stage, ["probThreshold"] = 0.5, ["boundedMargin"] = margin,
                ["minDwellMin"] = 10, ["driftThreshold"] = 1,
            },
        };
        if (now is { } n) ctx.Now = n;
        return ctx;
    }

    /// <summary>Minimal in-memory <see cref="IBlockContext"/> for unit-testing a block in isolation.</summary>
    private sealed class FakeContext : IBlockContext
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public string? ZoneId { get; set; }
        public string? ZoneKind { get; set; }
        public Dictionary<string, double> Params { get; } = new();
        public Dictionary<string, object?> Inputs { get; } = new();
        public Dictionary<string, object?> Commands { get; } = new();
        public Dictionary<string, object?> State { get; } = new();
        public Dictionary<string, object?> Emitted { get; } = new();

        public object? Get(string cap) => Emitted.TryGetValue(cap, out var v) ? v : null;

        public object? Read(string inputPort) => Inputs.TryGetValue(inputPort, out var v) ? v : null;
        public double? ReadNumber(string inputPort) => Read(inputPort) is double d ? d : null;
        public double Param(string key, double fallback) => Params.TryGetValue(key, out var v) ? v : fallback;
        public object? Commanded(string capabilityId) => Commands.TryGetValue(capabilityId, out var v) ? v : null;
        public void Emit(string capabilityId, object? value) => Emitted[capabilityId] = value;
        public T? GetState<T>(string key) => State.TryGetValue(key, out var v) && v is T t ? t : default;
        public void SetState<T>(string key, T value) => State[key] = value;
        public void Log(string message) { }
    }
}
