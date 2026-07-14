// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Expressions;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Tests for the tiny expression language and the <c>expression</c> block (roadmap Epic 2Q, Phase 3):
/// arithmetic + precedence, functions, ternary/logic, locals and multiple outputs, the <c>prev()</c>/<c>dt</c>
/// built-ins, missing-input hold, NaN/Inf guarding, and parse-error surfacing.
/// </summary>
public sealed class ExpressionTests
{
    // ── Engine ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Arithmetic_RespectsPrecedenceAndParentheses()
    {
        var p = ExpressionProgram.Parse("OUT1 = (IN1 + IN2) * IN3");
        var env = new FakeEnv { Inputs = { ["IN1"] = 1, ["IN2"] = 2, ["IN3"] = 3 } };
        Assert.Equal(9.0, p.Run(env)["OUT1"]);
    }

    [Fact]
    public void Functions_Clamp()
    {
        var p = ExpressionProgram.Parse("OUT1 = clamp(IN1, 0, 100)");
        Assert.Equal(100.0, p.Run(new FakeEnv { Inputs = { ["IN1"] = 150 } })["OUT1"]);
        Assert.Equal(0.0, p.Run(new FakeEnv { Inputs = { ["IN1"] = -5 } })["OUT1"]);
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(15, 0)]
    public void TernaryAndComparison(double input, double expected)
    {
        var p = ExpressionProgram.Parse("OUT1 = IN1 > 20 ? 1 : 0");
        Assert.Equal(expected, p.Run(new FakeEnv { Inputs = { ["IN1"] = input } })["OUT1"]);
    }

    [Fact]
    public void BooleanLogic()
    {
        var p = ExpressionProgram.Parse("OUT1 = IN1 > 0 && IN2 < 10");
        Assert.Equal(1.0, p.Run(new FakeEnv { Inputs = { ["IN1"] = 5, ["IN2"] = 3 } })["OUT1"]);
        Assert.Equal(0.0, p.Run(new FakeEnv { Inputs = { ["IN1"] = 5, ["IN2"] = 20 } })["OUT1"]);
    }

    [Fact]
    public void Locals_AndMultipleOutputs()
    {
        var p = ExpressionProgram.Parse("""
            let x = IN1 * 2
            OUT1 = x + 1
            OUT2 = x - 1
            """);
        var r = p.Run(new FakeEnv { Inputs = { ["IN1"] = 10 } });
        Assert.Equal(21.0, r["OUT1"]);
        Assert.Equal(19.0, r["OUT2"]);
    }

    [Fact]
    public void MissingInput_YieldsNoOutputs()
    {
        var p = ExpressionProgram.Parse("OUT1 = IN1 + 1");
        Assert.Empty(p.Run(new FakeEnv())); // IN1 unbound → program holds
    }

    [Theory]
    [InlineData("OUT1 =")]       // no expression
    [InlineData("OUT1 = IN1 +")] // dangling operator
    [InlineData("OUT1 = (IN1")]  // unbalanced parenthesis
    [InlineData("x = 5")]        // no OUT assignment
    public void ParseErrors_Throw(string script)
    {
        Assert.ThrowsAny<System.FormatException>(() => ExpressionProgram.Parse(script));
    }

    // ── Block (prev/dt, NaN guard, parse-error surfacing) ────────────────────────────────────────

    [Fact]
    public void Block_PrevAccumulates()
    {
        var block = new ExpressionBlock();
        var ctx = new FakeBlockCtx { Options = { ["script"] = "OUT1 = prev(OUT1) + 1" } };
        block.Tick(ctx); Assert.Equal(1.0, ctx.GetNumber("OUT1"));
        block.Tick(ctx); Assert.Equal(2.0, ctx.GetNumber("OUT1"));
        block.Tick(ctx); Assert.Equal(3.0, ctx.GetNumber("OUT1"));
    }

    [Fact]
    public void Block_EmaOneLiner_UsesDt()
    {
        // A full EMA written in one line using dt and prev — proves the built-ins compose.
        var block = new ExpressionBlock();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ctx = new FakeBlockCtx
        {
            Options = { ["script"] = "OUT1 = prev(OUT1) + (1 - exp(-dt/300)) * (IN1 - prev(OUT1))" },
            Now = t0, Inputs = { ["IN1"] = 10.0 },
        };
        block.Tick(ctx);
        Assert.Equal(0.0, ctx.GetNumber("OUT1"));            // first tick dt=0 → stays at prev (0)

        ctx.Now = t0.AddSeconds(300); block.Tick(ctx);
        var v = ctx.GetNumber("OUT1")!.Value;
        Assert.InRange(v, 6.0, 6.6);                         // ~ (1-e^-1)*10 ≈ 6.32
    }

    [Fact]
    public void Block_GuardsAgainstInfinity()
    {
        var block = new ExpressionBlock();
        var ctx = new FakeBlockCtx { Options = { ["script"] = "OUT1 = IN1 / IN2" }, Inputs = { ["IN1"] = 5.0, ["IN2"] = 0.0 } };
        block.Tick(ctx);
        Assert.Null(ctx.GetNumber("OUT1")); // 5/0 = ∞ → not emitted onto an actuator
    }

    [Fact]
    public void Block_BadScript_SurfacesAsError()
    {
        var block = new ExpressionBlock();
        var ctx = new FakeBlockCtx { Options = { ["script"] = "OUT1 = IN1 +" } };
        Assert.ThrowsAny<Exception>(() => block.Tick(ctx)); // parse error → block health error
    }

    private sealed class FakeEnv : IExprEnv
    {
        public Dictionary<string, double> Inputs { get; } = new();
        public Dictionary<string, double> Prevs { get; } = new();
        public double Dt { get; set; }
        public double? Input(string name) => Inputs.TryGetValue(name, out var v) ? v : null;
        public double Prev(string outputName) => Prevs.TryGetValue(outputName, out var v) ? v : 0;
    }
}
