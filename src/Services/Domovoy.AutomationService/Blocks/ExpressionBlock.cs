// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Expressions;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Custom expression block (roadmap Epic 2Q, Phase 3) — the escape hatch when no primitive/template fits. The
/// user writes a few lines of the tiny <see cref="ExpressionProgram"/> language in the <c>script</c> option
/// (e.g. <c>OUT1 = clamp((IN1 + IN2) * IN3, 0, 100)</c>), wiring the block's <c>IN1..IN4</c> inputs to devices
/// or other blocks and its <c>OUT1/OUT2</c> outputs onward. The script is parsed once when the instance loads;
/// a parse error surfaces as the block's health error. It is opaque to the visual graph and to explainability,
/// so it complements — not replaces — the introspectable primitives.
/// </summary>
public sealed class ExpressionType : IBlockType
{
    public string TypeId => "expression";
    public string Category => BlockCategories.Math;
    public string Title => "Expression (custom script)";
    public string Description => "Compute outputs from inputs with a few lines of arithmetic — OUT1 = (IN1 + IN2) * IN3.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("IN1", CapabilityKind.Number, "Input 1", Optional: true),
        new BlockPortSpec("IN2", CapabilityKind.Number, "Input 2", Optional: true),
        new BlockPortSpec("IN3", CapabilityKind.Number, "Input 3", Optional: true),
        new BlockPortSpec("IN4", CapabilityKind.Number, "Input 4", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number("OUT1", writable: false),
        WellKnownCapabilities.Number("OUT2", writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("script", BlockOptionKind.Text, "OUT1 = IN1",
            "Expression program. Lines: `OUTn = expr` (output) or `name = expr` (local). Supports + - * / %, "
            + "comparisons, && || !, a ? b : c, functions (min max abs clamp round floor ceil sqrt pow avg exp), "
            + "and dt / prev(OUTn)."),
    };

    public IBlock Create() => new ExpressionBlock();
}

public sealed class ExpressionBlock : IBlock
{
    private bool _parsed;
    private ExpressionProgram? _program;
    private Exception? _parseError;

    public void Tick(IBlockContext ctx)
    {
        if (!_parsed)
        {
            _parsed = true;
            try { _program = ExpressionProgram.Parse(ctx.Option("script") ?? ""); }
            catch (Exception ex) { _parseError = ex; }
        }
        // Surface a bad script as the block's health error (visible in the authoring UI) rather than silently doing nothing.
        if (_parseError is not null) throw new InvalidOperationException($"expression parse error: {_parseError.Message}");
        if (_program is null) return;

        var dt = ctx.GetState<bool>("seeded") ? Math.Max(0, (ctx.Now - ctx.GetState<DateTimeOffset>("t")).TotalSeconds) : 0;
        var outputs = _program.Run(new Env(ctx, dt));

        foreach (var (name, value) in outputs)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) continue; // never emit garbage onto an actuator
            ctx.Emit(name, Math.Round(value, 4));
            ctx.SetState("prev_" + name, value);
        }

        ctx.SetState("t", ctx.Now);
        ctx.SetState("seeded", true);
    }

    /// <summary>Bridges the expression language to the block context: inputs, previous outputs, tick gap.</summary>
    private sealed class Env : IExprEnv
    {
        private readonly IBlockContext _ctx;
        public Env(IBlockContext ctx, double dt) { _ctx = ctx; Dt = dt; }

        public double Dt { get; }
        public double? Input(string name) => _ctx.ReadNumber(name);
        public double Prev(string outputName) => _ctx.GetState<double>("prev_" + outputName);
    }
}
