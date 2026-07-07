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

    // A composite ("smoothed") that another composite can nest as a stage (roadmap Epic 1H E2, composite-in-composite).
    private static IBlockType? ResolveWithNested(string id) => id switch
    {
        "smoothed" => new CompositeBlockType(
            BlockDsl.Parse("smoothed", "Smoothed", "EWMA only", "input(temperature) |> ewma_filter(tau=300)", Resolve),
            Resolve),
        _ => Resolve(id),
    };

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

    // Resolver with every primitive the branching tests reference.
    private static IBlockType? ResolveGraph(string id) => id switch
    {
        "co2_ventilation" => new Co2VentilationType(),
        "irrigation_sequencer" => new IrrigationSequencerType(),
        _ => Resolve(id),
    };

    // Graph form: two independent branches (climate + air), each surfaced as its own renamed output.
    private const string ClimateAir = """
        in temperature
        in co2
        heat = thermostat(setpoint=21, hysteresis=0.5) <- temperature
        vent = co2_ventilation(threshold=800, hysteresis=100) <- co2
        out heat_demand = heat.on_off
        out vent_demand = vent.on_off
        """;

    [Fact]
    public void Graph_Parses_BranchingTopology()
    {
        var def = BlockDsl.Parse("climate_air", "Climate+Air", "desc", ClimateAir, ResolveGraph);

        Assert.Equal(2, def.Inputs.Count);
        Assert.Equal(2, def.Nodes.Count);
        Assert.Equal(2, def.Outputs.Count);
        Assert.All(def.Inputs, i => Assert.Equal(CapabilityKind.Number, i.Kind)); // kinds inferred from consuming ports
        Assert.Contains(def.Outputs, o => o.Capability.Id == "heat_demand" && o.Source == "heat#on_off");
        Assert.Contains(def.Outputs, o => o.Capability.Id == "vent_demand" && o.Source == "vent#on_off");
    }

    [Fact]
    public void Graph_RunsIndependentBranches()
    {
        var def = BlockDsl.Parse("climate_air", "Climate+Air", "desc", ClimateAir, ResolveGraph);
        var block = new CompositeBlock(def, ResolveGraph);

        var hot = new FakeCompositeContext { Inputs = { ["temperature"] = 18.0, ["co2"] = 1000.0 } };
        block.Tick(hot);
        Assert.Equal(true, hot.Get("heat_demand")); // 18 < 21 → heat
        Assert.Equal(true, hot.Get("vent_demand")); // 1000 > 900 → vent

        var calm = new FakeCompositeContext { Inputs = { ["temperature"] = 25.0, ["co2"] = 500.0 } };
        block.Tick(calm);
        Assert.Equal(false, calm.Get("heat_demand")); // 25 > 21.5 → no heat
        Assert.Equal(false, calm.Get("vent_demand")); // 500 < 700 → no vent
    }

    [Fact]
    public void Graph_FanOut_MultiZoneIrrigation()
    {
        // One shared inhibit (rain) feeds three sequencers, each a distinct valve output — the multi-zone shape (1D).
        const string multiZone = """
            in rain
            z1 = irrigation_sequencer(intervalHours=24, runMinutes=15) <- rain
            z2 = irrigation_sequencer(intervalHours=24, runMinutes=20) <- rain
            z3 = irrigation_sequencer(intervalHours=48, runMinutes=10) <- rain
            out zone1 = z1.on_off
            out zone2 = z2.on_off
            out zone3 = z3.on_off
            """;
        var def = BlockDsl.Parse("multi_zone", "Multi-zone irrigation", "desc", multiZone, ResolveGraph);

        Assert.Single(def.Inputs);
        Assert.Equal(3, def.Nodes.Count);
        Assert.All(def.Nodes, n => Assert.Equal("$rain", n.Inputs["inhibit"]));       // fan-out to all three
        Assert.Equal(new[] { "zone1", "zone2", "zone3" }, def.Outputs.Select(o => o.Capability.Id)); // distinct outputs
    }

    [Fact]
    public void Graph_MultiZone_WatersZonesOnIndependentSchedules()
    {
        // The built-in irrigation_multizone shape: z1/z2 water every 24h, z3 every 48h, all skipping on rain.
        const string multiZone = """
            in inhibit
            z1 = irrigation_sequencer(intervalHours=24, runMinutes=15) <- inhibit
            z2 = irrigation_sequencer(intervalHours=24, runMinutes=20) <- inhibit
            z3 = irrigation_sequencer(intervalHours=48, runMinutes=10) <- inhibit
            out zone1 = z1.on_off
            out zone2 = z2.on_off
            out zone3 = z3.on_off
            """;
        var def = BlockDsl.Parse("irrigation_multizone", "Multi-zone", "desc", multiZone, ResolveGraph);
        var block = new CompositeBlock(def, ResolveGraph);

        var t0 = new DateTimeOffset(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);
        var ctx = new FakeCompositeContext { Now = t0 }; // one shared context so per-node state persists across ticks

        block.Tick(ctx); // first tick seeds each sequencer's clock → all closed
        Assert.Equal(false, ctx.Get("zone1"));
        Assert.Equal(false, ctx.Get("zone3"));

        ctx.Now = t0.AddHours(25); // past the 24h interval, short of 48h
        block.Tick(ctx);
        Assert.Equal(true, ctx.Get("zone1"));  // 25h ≥ 24h → z1 waters
        Assert.Equal(true, ctx.Get("zone2"));  // z2 also on a 24h interval
        Assert.Equal(false, ctx.Get("zone3")); // 25h < 48h → z3 still idle (independent schedule)
    }

    [Fact]
    public void Graph_RoundTrips()
    {
        var def = BlockDsl.Parse("climate_air", "t", "d", ClimateAir, ResolveGraph);
        var text = BlockDsl.SerializeGraph(def);
        var reparsed = BlockDsl.Parse("climate_air", "t", "d", text, ResolveGraph);
        Assert.Equal(BlockDsl.SerializeGraph(def), BlockDsl.SerializeGraph(reparsed));
    }

    [Fact]
    public void Composite_CanNestAnotherComposite()
    {
        // Outer pipeline uses the "smoothed" composite as a stage, then a thermostat — nesting one composite in another.
        const string nested = "input(temperature) |> smoothed() |> thermostat(setpoint=21, hysteresis=0.5)";
        var def = BlockDsl.Parse("nested_loop", "Nested loop", "desc", nested, ResolveWithNested);
        Assert.Equal("smoothed", def.Nodes[0].TypeId); // the inner composite is a first-class stage

        var block = new CompositeBlock(def, ResolveWithNested);
        var ctx = new FakeCompositeContext { Inputs = { ["temperature"] = 18.0 } };
        block.Tick(ctx);

        // Nested EWMA seeds to 18 → thermostat demands heat, exactly like the flat climate loop at 18°.
        Assert.Equal(true, ctx.Get(CapabilityIds.OnOff));
    }

    [Fact]
    public void Parse_UnknownReferencedType_ThrowsUnknownBlockType()
    {
        // The distinct exception is what lets catalog registration defer a composite that references a
        // not-yet-registered composite (vs. dropping a genuinely malformed one).
        var ex = Assert.Throws<UnknownBlockTypeException>(() =>
            BlockDsl.Parse("x", "x", "", "input(t) |> no_such_block()", Resolve));
        Assert.Equal("no_such_block", ex.TypeId);
    }

    [Fact]
    public void Parse_MalformedDsl_ThrowsPlainFormatException()
    {
        // A single stage (no pipe) is malformed, not an unknown-type case — exact FormatException, not the subclass.
        Assert.Throws<FormatException>(() => BlockDsl.Parse("x", "x", "", "input(t)", Resolve));
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
