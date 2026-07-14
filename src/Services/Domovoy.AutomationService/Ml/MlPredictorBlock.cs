// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Ml.Governors;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// ML predictor as a plain source block (roadmap Epic 2Q, Phase 3): it emits a model's current prediction for
/// a configured <c>target</c> as an ordinary numeric <c>value</c>, so ML becomes just another input any block
/// can consume (feed it into a <c>clamp</c>, a <c>select</c>, a <c>pid</c> setpoint, an <c>expression</c>). It
/// carries no authority staging or drift monitor — that governance lives in the ML governors when the output
/// drives a loop. The model is resolved along the instance's zone → zone_kind → global scope chain, honoring a
/// pinned version. Emits nothing until a model is loaded.
/// </summary>
public sealed class MlPredictorType : IBlockType
{
    private readonly MlModelService _models;

    public MlPredictorType(MlModelService models) => _models = models;

    public string TypeId => "ml_predictor";
    public string Category => BlockCategories.Ml;
    public string Title => "ML predictor (source)";
    public string Description => "Emits a trained model's prediction for a target as a numeric signal for other blocks.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = Array.Empty<BlockPortSpec>();

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.Number("value", writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec(MlGovernorBase.ModelVersionParam, 0, null, 0, null, "Pinned model version (0 = latest)"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("target", BlockOptionKind.Text, CapabilityIds.Temperature, "Trained target capability to predict"),
    };

    public IBlock Create() => new MlPredictorBlock(_models);
}

public sealed class MlPredictorBlock : IBlock
{
    private readonly MlModelService _models;

    public MlPredictorBlock(MlModelService models) => _models = models;

    public void Tick(IBlockContext ctx)
    {
        var target = ctx.Option("target");
        if (string.IsNullOrWhiteSpace(target)) target = CapabilityIds.Temperature;

        if (_models.TryPredict(target, ctx.Now, MlGovernorCore.BuildScopeChain(ctx), MlGovernorCore.PinnedVersion(ctx), out var v))
            ctx.Emit("value", Math.Round(v, 4));
    }
}
