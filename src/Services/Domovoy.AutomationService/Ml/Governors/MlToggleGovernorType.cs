using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Catalog type for a <see cref="MlToggleGovernor"/> (roadmap Epic 2I, Phase 2). Like the setpoint-governor
/// type, one class yields many instances: the same code becomes an ML light scheduler, an ML fan governor,
/// etc., by binding a different boolean output. Registered in the <see cref="BlockCatalog"/> from config.
/// </summary>
public sealed class MlToggleGovernorType : IBlockType
{
    private readonly Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> _predict;
    private readonly string _measuredInput;
    private readonly Capability _output;

    public MlToggleGovernorType(
        string typeId, string title, string description, string measuredInput, Capability output,
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> predict)
    {
        TypeId = typeId;
        Title = title;
        Description = description;
        _measuredInput = measuredInput;
        _output = output;
        _predict = predict;

        Inputs = new[]
        {
            new BlockPortSpec(measuredInput, CapabilityKind.Boolean, "Measured on/off state — for the drift monitor"),
        };
        Outputs = new[]
        {
            output, // bound boolean output (drives the deterministic loop) — emitted only when active
            WellKnownCapabilities.Number(MlGovernorBase.ProposedSetpoint, null, 0, 1, step: 0.01),
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
        new BlockParamSpec("probThreshold", 0.5, null, 0, 1, "Probability at/above which the device is commanded on"),
        new BlockParamSpec("boundedMargin", 0.2, null, 0, 0.5, "Bounded-Active: extra probability margin required to flip (hysteresis)"),
        new BlockParamSpec("minDwellMin", 10, "min", 0, 1440, "Minimum time to hold a state before flipping (anti-chatter)"),
        new BlockParamSpec("driftThreshold", 0.5, null, 0, 1, "Mean disagreement rate that auto-demotes to Shadow"),
        new BlockParamSpec("driftWindowMin", 60, "min", 5, 1440, "Rolling window for the drift mean"),
    };

    public IBlock Create() => new MlToggleGovernor(_predict, _measuredInput, _output.Id);
}
