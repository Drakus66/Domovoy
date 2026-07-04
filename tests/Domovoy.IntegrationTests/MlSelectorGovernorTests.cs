using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Ml.Governors;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the ML selector governor (roadmap Epic 2I, Phase 3): class proposal, Shadow gating, Bounded
/// adjacency clamp along the value order, and misclassification-rate drift auto-demote. Pure — the predictor
/// (a class label) is a stub.
/// </summary>
public sealed class MlSelectorGovernorTests
{
    private const string Out = "hvac_mode";
    private static readonly string[] Values = { "off", "eco", "comfort", "boost" };

    private static MlSelectorGovernor Governor(Func<DateTimeOffset, string?> predict) =>
        new((now, _, _) => predict(now), measuredInput: "mode", boundOutput: Out, values: Values);

    [Fact]
    public void Shadow_EmitsProposal_ButNoBoundCommand()
    {
        var ctx = new FakeContext { Params = { ["stage"] = 0 } };
        Governor(_ => "comfort").Tick(ctx);

        Assert.Equal("comfort", ctx.Get(MlGovernorBase.ProposedSetpoint));
        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(Out));
    }

    [Fact]
    public void Full_AppliesProposedClass_Directly()
    {
        var ctx = new FakeContext
        {
            Params = { ["stage"] = 2, ["driftThreshold"] = 1 },
            Commands = { [Out] = "off" },
        };
        Governor(_ => "boost").Tick(ctx);

        Assert.Equal(2d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.Equal("boost", ctx.Get(Out)); // Full → no adjacency limit
    }

    [Fact]
    public void Bounded_ClampsToAdjacentClass_AlongValueOrder()
    {
        // Current "off" (index 0), proposed "boost" (index 3), maxClassStep 1 → clamp to "eco" (index 1).
        var ctx = new FakeContext
        {
            Params = { ["stage"] = 1, ["maxClassStep"] = 1, ["driftThreshold"] = 1 },
            Commands = { [Out] = "off" },
        };
        Governor(_ => "boost").Tick(ctx);

        Assert.Equal(1d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.Equal("eco", ctx.Get(Out));
    }

    [Fact]
    public void Drift_AutoDemotesToShadow_WhenPredictedClassDisagrees()
    {
        // Predicts "boost" while reality is "off" → misclassification rate 1.0 > threshold 0.5 → Shadow.
        var ctx = new FakeContext
        {
            Params = { ["stage"] = 2, ["driftThreshold"] = 0.5, ["driftWindowMin"] = 60 },
            Inputs = { ["mode"] = "off" },
        };
        Governor(_ => "boost").Tick(ctx);

        Assert.Equal(0d, ctx.Get(MlGovernorBase.EffectiveStage));
        Assert.False(ctx.Emitted.ContainsKey(Out));
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
