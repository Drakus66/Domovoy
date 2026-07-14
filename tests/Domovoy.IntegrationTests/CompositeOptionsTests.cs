// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Blocks.Composite;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Tests that string options flow through the composite DSL (roadmap Epic 2Q, Phase 4): a non-numeric argument
/// (e.g. <c>invert=true</c>, <c>op=lt</c>) parses onto the node as an option and reaches the primitive at runtime.
/// </summary>
public sealed class CompositeOptionsTests
{
    private static IBlockType? Resolve(string id) => id switch
    {
        "ewma_filter" => new EwmaFilterType(),
        "hysteresis" => new HysteresisType(),
        "comparator" => new ComparatorType(),
        "median_filter" => new MedianFilterType(),
        _ => null,
    };

    [Fact]
    public void Dsl_ParsesStringOption_OntoNode()
    {
        var def = BlockDsl.Parse("cooling_relay", "Cooling", "desc",
            "input(temperature) |> hysteresis(high=25, low=24, invert=true)", Resolve);

        var node = Assert.Single(def.Nodes);
        Assert.Equal(25, node.Params["high"]);
        Assert.Equal(24, node.Params["low"]);
        Assert.NotNull(node.Options);
        Assert.Equal("true", node.Options!["invert"]); // non-numeric arg → option, not a param
    }

    [Fact]
    public void Composite_AppliesInvertOption_AtRuntime()
    {
        var def = BlockDsl.Parse("cooling_relay", "Cooling", "desc",
            "input(temperature) |> hysteresis(high=25, low=24, invert=true)", Resolve);
        var block = new CompositeBlock(def, Resolve);

        var ctx = new FakeBlockCtx { Inputs = { ["temperature"] = 23.0 } };
        block.Tick(ctx);
        Assert.Equal(true, ctx.Get("state")); // inverted: below low → ON (cooling sense)

        ctx.Inputs["temperature"] = 27.0;
        block.Tick(ctx);
        Assert.Equal(false, ctx.Get("state")); // above high → OFF
    }

    [Fact]
    public void Dsl_RoundTripsOptions()
    {
        var def = BlockDsl.Parse("cmp", "Cmp", "desc",
            "input(x) |> comparator(threshold=20, op=lt)", Resolve);
        var reparsed = BlockDsl.Parse("cmp", "Cmp", "desc", BlockDsl.Serialize(def), Resolve);
        Assert.Equal("lt", reparsed.Nodes[^1].Options!["op"]);
        Assert.Equal(20, reparsed.Nodes[^1].Params["threshold"]);
    }

    [Fact]
    public void Dsl_ParsesPrimitiveThermostatGraph()
    {
        var def = BlockDsl.Parse("smart_thermostat", "Thermostat", "desc", """
            in temperature
            f = ewma_filter(tau=300) <- temperature
            h = hysteresis(high=21.5, low=20.5) <- f.value
            out heat = h.state
            """, Resolve);

        Assert.Equal(2, def.Nodes.Count);
        Assert.Equal("ewma_filter", def.Nodes[0].TypeId);
        Assert.Equal("hysteresis", def.Nodes[1].TypeId);
        Assert.Equal("f#value", def.Nodes[1].Inputs["in"]); // hysteresis reads the filter's output (composition)
        var output = Assert.Single(def.Outputs);
        Assert.Equal("heat", output.Capability.Id);         // renamed composite output
    }
}
