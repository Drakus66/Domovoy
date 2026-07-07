// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Catalog type for a <see cref="MlSelectorGovernor"/> (roadmap Epic 2I, Phase 3). One class, many instances:
/// the same code becomes an ML HVAC-mode selector, an ML fan-level selector, etc., by binding a different enum
/// output. The Bounded-Active adjacency order comes from the output capability's declared <c>Values</c>.
/// </summary>
public sealed class MlSelectorGovernorType : IBlockType
{
    private readonly Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, string?> _predict;
    private readonly string _measuredInput;
    private readonly Capability _output;
    private readonly IReadOnlyList<string> _values;

    public MlSelectorGovernorType(
        string typeId, string title, string description, string measuredInput, Capability output,
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, string?> predict)
    {
        TypeId = typeId;
        Title = title;
        Description = description;
        _measuredInput = measuredInput;
        _output = output;
        _predict = predict;
        _values = output.Attributes.TryGetValue(CapabilityAttributeKeys.Values, out var v) && v is IReadOnlyList<string> list
            ? list
            : Array.Empty<string>();

        Inputs = new[]
        {
            new BlockPortSpec(measuredInput, CapabilityKind.Enum, "Measured current value — for the drift monitor"),
        };
        Outputs = new[]
        {
            output, // bound enum output (drives the deterministic loop) — emitted only when active
            WellKnownCapabilities.Enum(MlGovernorBase.ProposedSetpoint, _values, writable: false),
            WellKnownCapabilities.Number(MlGovernorBase.EffectiveStage, null, 0, 2, step: 1),
            WellKnownCapabilities.Number(MlGovernorBase.Drift, null, 0, 1, step: 0.01),
        };
    }

    public string TypeId { get; }
    public string Title { get; }
    public string Description { get; }

    public IReadOnlyList<BlockPortSpec> Inputs { get; }
    public IReadOnlyList<Capability> Outputs { get; }

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("stage", 0, null, 0, 2, "Authority stage: 0 = Shadow, 1 = Bounded-Active, 2 = Full"),
        new BlockParamSpec("maxClassStep", 1, null, 1, 10, "Bounded-Active: max positions to move along the value order per change"),
        new BlockParamSpec("driftThreshold", 0.5, null, 0, 1, "Mean misclassification rate that auto-demotes to Shadow"),
        new BlockParamSpec("driftWindowMin", 60, "min", 5, 1440, "Rolling window for the drift mean"),
        new BlockParamSpec(MlGovernorBase.ModelVersionParam, 0, null, 0, 100000, "Pinned model version (0 = latest); set via the approval queue (2C)"),
    };

    public IBlock Create() => new MlSelectorGovernor(_predict, _measuredInput, _output.Id, _values);
}
