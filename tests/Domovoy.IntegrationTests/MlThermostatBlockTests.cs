using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Ml;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the ML setpoint governor (roadmap Epic 2B): authority stages, floor clamps, the bounded
/// band, and drift / no-model auto-demote to Shadow. Pure — no infrastructure, the predictor is a stub.
/// </summary>
public sealed class MlThermostatBlockTests
{
    private const double FloorMin = 16;
    private const double FloorMax = 26;

    [Fact]
    public void Shadow_EmitsProposal_ButNoBoundSetpoint()
    {
        var ctx = Tick(stage: 0, predicted: 23, measured: 23);

        Assert.Equal(23d, ctx.Get(MlThermostatBlock.ProposedSetpoint));
        Assert.Equal(0d, ctx.Get(MlThermostatBlock.EffectiveStage));
        // No bound output → BlockRuntime publishes no command (Shadow = no actuation).
        Assert.False(ctx.Emitted.ContainsKey(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Bounded_ClampsProposalToBandAroundBaseline()
    {
        // Proposal 25, baseline 21, band 1.5 → clamped to 22.5.
        var ctx = Tick(stage: 1, predicted: 25, measured: 21, baseline: 21, band: 1.5);

        Assert.Equal(1d, ctx.Get(MlThermostatBlock.EffectiveStage));
        Assert.Equal(22.5, ctx.Get(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Full_AppliesProposal_ClampedOnlyByFloor()
    {
        // Proposal above the floor max → clamped to the floor, not the band.
        var ctx = Tick(stage: 2, predicted: 40, measured: 22, baseline: 21, band: 1.5);

        Assert.Equal(2d, ctx.Get(MlThermostatBlock.EffectiveStage));
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
        var block = new MlThermostatBlock(_ => null, FloorMin, FloorMax);
        var ctx = new FakeContext { Params = { ["stage"] = 2 } };
        block.Tick(ctx);

        Assert.Equal(0d, ctx.Get(MlThermostatBlock.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(CapabilityIds.TemperatureSetpoint));
    }

    [Fact]
    public void Drift_AutoDemotesActiveStageToShadow()
    {
        // Active (Full) but the model predicts 24 while reality sits at 18 → mean error 6 > threshold 3.
        var block = new MlThermostatBlock(_ => 24, FloorMin, FloorMax);
        var ctx = new FakeContext
        {
            Params = { ["stage"] = 2, ["driftThreshold"] = 3, ["driftWindowMin"] = 60 },
            Inputs = { ["temperature"] = 18.0 },
        };

        block.Tick(ctx);

        Assert.Equal(0d, ctx.Get(MlThermostatBlock.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(CapabilityIds.TemperatureSetpoint)); // demoted → no command
        Assert.True((double)ctx.Get(MlThermostatBlock.Drift)! >= 3);
    }

    private static FakeContext Tick(
        int stage, double predicted, double measured, double baseline = 21, double band = 1.5)
    {
        var block = new MlThermostatBlock(_ => predicted, FloorMin, FloorMax);
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
