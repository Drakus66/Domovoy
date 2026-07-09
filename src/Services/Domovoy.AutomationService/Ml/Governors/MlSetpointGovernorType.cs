// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Catalog type for a <see cref="MlSetpointGovernor"/> (roadmap Epic 2I). One class, many instances: the same
/// type, constructed with a different monitored input + commanded output, yields the ML thermostat, an ML
/// CO₂-setpoint, an ML humidity-setpoint, etc. The <see cref="BlockCatalog"/> registers concrete instances
/// from a config list, so a new ML-governed setpoint is configuration, not a new bespoke block class.
/// </summary>
public sealed class MlSetpointGovernorType : IBlockType, IMlGovernorBlockType
{
    private readonly Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, double?> _predict;
    private readonly string _measuredInput;
    private readonly Capability _output;
    private readonly double _floorMin;
    private readonly double _floorMax;

    /// <param name="typeId">Stable type id (e.g. <c>ml_thermostat</c>).</param>
    /// <param name="title">Display title.</param>
    /// <param name="description">Display description.</param>
    /// <param name="measuredInput">Input port carrying the measured signal (e.g. <c>temperature</c>).</param>
    /// <param name="output">Writable capability the governor commands (e.g. temperature_setpoint).</param>
    /// <param name="floorMin">Safety floor minimum.</param>
    /// <param name="floorMax">Safety floor maximum.</param>
    /// <param name="predict">Model predictor (time → value, or null when no model is loaded).</param>
    public MlSetpointGovernorType(
        string typeId, string title, string description, string measuredInput, Capability output,
        double floorMin, double floorMax, Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, double?> predict)
    {
        TypeId = typeId;
        Title = title;
        Description = description;
        _measuredInput = measuredInput;
        _output = output;
        _floorMin = floorMin;
        _floorMax = floorMax;
        _predict = predict;

        var unit = output.Attributes.TryGetValue(CapabilityAttributeKeys.Unit, out var u) ? u as string : null;
        Inputs = new[]
        {
            new BlockPortSpec(measuredInput, CapabilityKind.Number, "Measured (ideally filtered) signal — for the drift monitor"),
        };
        Outputs = new[]
        {
            output, // bound output (drives the deterministic loop) — emitted only when active
            WellKnownCapabilities.Number(MlGovernorBase.ProposedSetpoint, unit, floorMin, floorMax, step: 0.1),
            WellKnownCapabilities.Number(MlGovernorBase.EffectiveStage, null, 0, 2, step: 1),
            WellKnownCapabilities.Number(MlGovernorBase.Drift, unit, 0, null, step: 0.01),
        };
    }

    public string TypeId { get; }
    public string Title { get; }
    public string Description { get; }

    /// <summary>The ML target this governor consumes = its measured input (Epic 2P): the model predicts the monitored signal.</summary>
    public string MlTargetCapability => _measuredInput;

    public IReadOnlyList<BlockPortSpec> Inputs { get; }
    public IReadOnlyList<Capability> Outputs { get; }

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("stage", 0, null, 0, 2, "Authority stage: 0 = Shadow, 1 = Bounded-Active, 2 = Full"),
        new BlockParamSpec("baseline", 21, null, -50, 100, "Trusted baseline (Bounded band is centred here; tracks the loop's live setpoint)"),
        new BlockParamSpec("band", 1.5, null, 0.1, 50, "Bounded-Active: max deviation from baseline the ML may apply"),
        new BlockParamSpec("driftThreshold", 3, null, 0.5, 100, "Mean prediction error that auto-demotes to Shadow"),
        new BlockParamSpec("driftWindowMin", 60, "min", 5, 1440, "Rolling window for the drift mean"),
        new BlockParamSpec(MlGovernorBase.ModelVersionParam, 0, null, 0, 100000, "Pinned model version (0 = latest); set via the approval queue (2C)"),
    };

    public IBlock Create() =>
        new MlSetpointGovernor(_predict, _floorMin, _floorMax, _measuredInput, _output.Id);
}
