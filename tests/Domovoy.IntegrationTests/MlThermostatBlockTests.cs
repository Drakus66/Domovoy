// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Ml.Governors;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the ML setpoint governor (roadmap Epic 2B/2I): authority stages, floor clamps, the bounded
/// band, and drift / no-model auto-demote to Shadow. Pure — no infrastructure, the predictor is a stub.
/// Exercises the generic <see cref="MlSetpointGovernor"/> wired as the temperature thermostat instance.
/// </summary>
public sealed class MlThermostatBlockTests
{
    private const double FloorMin = 16;
    private const double FloorMax = 26;

    private static MlSetpointGovernor Governor(Func<DateTimeOffset, double?> predict) =>
        new((now, _, _) => predict(now), FloorMin, FloorMax,
            measuredInput: "temperature", boundOutput: CapabilityIds.TemperatureSetpoint);

    [Fact]
    public void Shadow_EmitsProposal_ButNoBoundSetpoint()
    {
        var ctx = Tick(stage: 0, predicted: 23, measured: 23);

        Assert.Equal(23d, ctx.Get(MlGovernorBase.ProposedSetpoint));
        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        // No bound output → BlockRuntime publishes no command (Shadow = no actuation).
        Assert.False(ctx.Emitted.ContainsKey(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Bounded_ClampsProposalToBandAroundBaseline()
    {
        // Proposal 25, baseline 21, band 1.5 → clamped to 22.5.
        var ctx = Tick(stage: 1, predicted: 25, measured: 21, baseline: 21, band: 1.5);

        Assert.Equal(1d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.Equal(22.5, ctx.Get(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Full_AppliesProposal_ClampedOnlyByFloor()
    {
        // Proposal above the floor max → clamped to the floor, not the band.
        var ctx = Tick(stage: 2, predicted: 40, measured: 22, baseline: 21, band: 1.5);

        Assert.Equal(2d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.Equal(FloorMax, ctx.Get(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Floor_ClampsBelowMin_EvenInFull()
    {
        var ctx = Tick(stage: 2, predicted: 5, measured: 18);
        Assert.Equal(FloorMin, ctx.Get(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void NoModel_AutoDemotesToShadow()
    {
        var block = Governor(_ => null);
        var ctx = new FakeContext { Params = { ["stage"] = 2 } };
        block.Tick(ctx);

        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Drift_AutoDemotesActiveStageToShadow()
    {
        // Active (Full) but the model predicts 24 while reality sits at 18 → mean error 6 > threshold 3.
        var block = Governor(_ => 24);
        var ctx = new FakeContext
        {
            Params = { ["stage"] = 2, ["driftThreshold"] = 3, ["driftWindowMin"] = 60 },
            Inputs = { ["temperature"] = 18.0 },
        };

        block.Tick(ctx);

        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(CapabilityIds.TemperatureSetpoint)); // demoted → no command
        Assert.True((double)ctx.Get(MlGovernorBase.Drift)! >= 3);
    }

    [Fact]
    public void Predictor_IsCalledWith_ZoneThenKindThenGlobal_Chain()
    {
        // Epic 2I: a block in a zone of a known kind resolves its model along zone → zone_kind → global.
        IReadOnlyList<ModelScope>? seen = null;
        var block = new MlSetpointGovernor(
            (_, chain, _) => { seen = chain; return 22; }, FloorMin, FloorMax,
            measuredInput: "temperature", boundOutput: CapabilityIds.TemperatureSetpoint);
        var ctx = new FakeContext { ZoneId = "bedroom", ZoneKind = "room", Params = { ["stage"] = 0 } };

        block.Tick(ctx);

        Assert.NotNull(seen);
        Assert.Collection(seen!,
            s => { Assert.Equal(ModelScopeLevels.Zone, s.Level); Assert.Equal("bedroom", s.Key); },
            s => { Assert.Equal(ModelScopeLevels.ZoneKind, s.Level); Assert.Equal("room", s.Key); },
            s => Assert.Equal(ModelScopeLevels.Global, s.Level));
    }

    [Fact]
    public void Predictor_ReceivesPinnedModelVersion_FromParam()
    {
        // Epic 2C: an approved model_selection proposal patches Params["model_version"]; the governor threads it
        // to the predictor so MlModelService can serve the pinned version instead of latest.
        var seen = -1;
        var block = new MlSetpointGovernor(
            (_, _, version) => { seen = version; return 22; }, FloorMin, FloorMax,
            measuredInput: "temperature", boundOutput: CapabilityIds.TemperatureSetpoint);

        block.Tick(new FakeContext { Params = { ["stage"] = 0, ["model_version"] = 7 } });

        Assert.Equal(7, seen);
    }

    [Fact]
    public void Predictor_Chain_IsGlobalOnly_WhenNoZone()
    {
        IReadOnlyList<ModelScope>? seen = null;
        var block = new MlSetpointGovernor(
            (_, chain, _) => { seen = chain; return 22; }, FloorMin, FloorMax,
            measuredInput: "temperature", boundOutput: CapabilityIds.TemperatureSetpoint);

        block.Tick(new FakeContext { Params = { ["stage"] = 0 } });

        Assert.NotNull(seen);
        Assert.Single(seen!);
        Assert.Equal(ModelScopeLevels.Global, seen![0].Level);
    }

    private static FakeContext Tick(
        int stage, double predicted, double measured, double baseline = 21, double band = 1.5)
    {
        var block = Governor(_ => predicted);
        var ctx = new FakeContext
        {
            // High drift threshold so these tests isolate the staging/clamp logic (drift has its own test).
            Params = { ["stage"] = stage, ["baseline"] = baseline, ["band"] = band, ["driftThreshold"] = 100 },
            Inputs = { ["temperature"] = measured },
        };
        block.Tick(ctx);
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
