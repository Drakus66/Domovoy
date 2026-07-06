using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Blocks.Composite;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for E2 composite blocks + the pipe DSL (roadmap Epic 1H). Verifies the DSL parses/round-trips and
/// that a composite runs its internal subgraph (EWMA filter → thermostat) end to end. Pure — a tiny resolver
/// supplies the two built-in primitives, no infrastructure.
/// </summary>
public sealed class CompositeBlockTests
{
    private static IBlockType? Resolve(string id) => id switch
    {
        "ewma_filter" => new EwmaFilterType(),
        "thermostat" => new ThermostatType(),
        _ => null,
    };

    private const string ClimateLoop = "input(temperature) |> ewma_filter(tau=300) |> thermostat(setpoint=21, hysteresis=0.5)";

    [Fact]
    public void Dsl_Parses_PipelineIntoDefinition()
    {
        var def = BlockDsl.Parse("climate_loop", "Climate loop", "desc", ClimateLoop, Resolve);

        // One external input (temperature, numeric), two internal nodes, one output (the thermostat's on_off).
        var input = Assert.Single(def.Inputs);
        Assert.Equal("temperature", input.Name);
        Assert.Equal(CapabilityKind.Number, input.Kind);
        Assert.Equal(2, def.Nodes.Count);
        Assert.Equal("ewma_filter", def.Nodes[0].TypeId);
        Assert.Equal("thermostat", def.Nodes[1].TypeId);
        Assert.Equal(300, def.Nodes[0].Params["tau"]);

        var output = Assert.Single(def.Outputs);
        Assert.Equal(CapabilityIds.OnOff, output.Capability.Id);
        Assert.Equal("n1#on_off", output.Source);

        // The first node reads the composite input; the second reads the first node's output (composition).
        Assert.Equal("$temperature", def.Nodes[0].Inputs["in"]);
        Assert.Equal("n0#value", def.Nodes[1].Inputs["temperature"]);
    }

    [Fact]
    public void Dsl_RoundTrips()
    {
        var def = BlockDsl.Parse("climate_loop", "Climate loop", "desc", ClimateLoop, Resolve);
        var text = BlockDsl.Serialize(def);
        var reparsed = BlockDsl.Parse("climate_loop", "Climate loop", "desc", text, Resolve);
        Assert.Equal(BlockDsl.Serialize(def), BlockDsl.Serialize(reparsed));
    }

    [Fact]
    public void Composite_HeatsWhenBelowSetpoint()
    {
        var ctx = RunClimateLoop(temperature: 18);
        Assert.Equal(true, ctx.Get(CapabilityIds.OnOff)); // 18 < 21 → EWMA seeds 18 → thermostat demands heat
    }

    [Fact]
    public void Composite_IdleWhenAboveSetpoint()
    {
        var ctx = RunClimateLoop(temperature: 25);
        Assert.Equal(false, ctx.Get(CapabilityIds.OnOff)); // 25 > 21.5 → no heat demand
    }

    private static FakeCompositeContext RunClimateLoop(double temperature)
    {
        var def = BlockDsl.Parse("climate_loop", "Climate loop", "desc", ClimateLoop, Resolve);
        var block = new CompositeBlock(def, Resolve);
        var ctx = new FakeCompositeContext { Inputs = { ["temperature"] = temperature } };
        block.Tick(ctx);
        return ctx;
    }

    /// <summary>In-memory <see cref="IBlockContext"/> for the composite (parent) block.</summary>
    private sealed class FakeCompositeContext : IBlockContext
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public string? ZoneId { get; set; }
        public string? ZoneKind { get; set; }
        public Dictionary<string, object?> Inputs { get; } = new();
        public Dictionary<string, object?> Commands { get; } = new();
        public Dictionary<string, object?> State { get; } = new();
        public Dictionary<string, object?> Emitted { get; } = new();

        public object? Get(string cap) => Emitted.TryGetValue(cap, out var v) ? v : null;

        public object? Read(string inputPort) => Inputs.TryGetValue(inputPort, out var v) ? v : null;
        public double? ReadNumber(string inputPort) => Read(inputPort) is double d ? d : null;
        public double Param(string key, double fallback) => fallback;
        public object? Commanded(string capabilityId) => Commands.TryGetValue(capabilityId, out var v) ? v : null;
        public void Emit(string capabilityId, object? value) => Emitted[capabilityId] = value;
        public T? GetState<T>(string key) => State.TryGetValue(key, out var v) && v is T t ? t : default;
        public void SetState<T>(string key, T value) => State[key] = value;
        public void Log(string message) { }
    }
}
