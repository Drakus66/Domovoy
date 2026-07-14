// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// ML-setpoint control block (roadmap Epic 2A — the SDK hook for ML into the 1H runtime). On each tick it
/// asks the loaded schedule model (<see cref="MlModelService"/>) for the predicted target and emits it as a
/// <c>temperature_setpoint</c>, clamped to a safe range. This is the substrate demonstrator; the full
/// ML-thermostat (shadow→bounded→full, scorecard) is Epic 2B. Emits nothing until a model is trained/loaded.
/// </summary>
public sealed class MlSetpointType : IBlockType
{
    private readonly MlModelService _models;
    private readonly double _min;
    private readonly double _max;

    public MlSetpointType(MlModelService models, double min, double max)
    {
        _models = models;
        _min = min;
        _max = max;
    }

    public string TypeId => "ml_setpoint";
    public string Category => Blocks.BlockCategories.Ml;
    public string Title => "ML setpoint (learned schedule)";
    public string Description => "Emits a temperature setpoint predicted by the learned schedule model (Epic 2A).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = Array.Empty<BlockPortSpec>();

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.TemperatureSetpoint(min: 5, max: 35, step: 0.5),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = Array.Empty<BlockParamSpec>();

    public IBlock Create() => new MlSetpointBlock(_models, _min, _max);
}

public sealed class MlSetpointBlock : IBlock
{
    private readonly MlModelService _models;
    private readonly double _min;
    private readonly double _max;

    public MlSetpointBlock(MlModelService models, double min, double max)
    {
        _models = models;
        _min = min;
        _max = max;
    }

    public void Tick(IBlockContext ctx)
    {
        if (!_models.TryPredict(ctx.Now, out var predicted)) return; // no model yet
        var clamped = Math.Clamp(predicted, _min, _max); // safety clamp (the deterministic floor over ML)
        ctx.Emit(CapabilityIds.TemperatureSetpoint, Math.Round(clamped, 2));
    }
}
