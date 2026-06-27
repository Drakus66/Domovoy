using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Number → setpoint governor (roadmap Epic 2I) — the generalized form of the 2B ML-thermostat. Proposes a
/// learned numeric setpoint to a deterministic loop and is parameterized by which signal it monitors and which
/// writable capability it commands, so the same code governs a temperature, CO₂, humidity or irrigation
/// setpoint — the thermostat is just the <c>(temperature → temperature_setpoint)</c> instance, registered as
/// config in the catalog rather than written as a bespoke class.
///
/// <para>Bounded-Active clamps the proposal to <c>baseline±band</c>, where the baseline tracks the loop's live
/// commanded setpoint (falling back to the <c>baseline</c> param). Full passes the proposal through; the safety
/// floor clamps on every active stage.</para>
/// </summary>
public sealed class MlSetpointGovernor : MlGovernorBase
{
    private readonly double _floorMin;
    private readonly double _floorMax;

    public MlSetpointGovernor(
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> predict, double floorMin, double floorMax,
        string measuredInput, string boundOutput)
        : base(predict, measuredInput, boundOutput)
    {
        _floorMin = floorMin;
        _floorMax = floorMax;
    }

    protected override void EmitBound(IBlockContext ctx, double proposed, bool bounded)
    {
        var target = bounded ? Clamp(proposed, ctx) : proposed;
        target = Math.Round(Math.Clamp(target, _floorMin, _floorMax), 2);
        ctx.Emit(BoundOutput, target); // bound → commands the deterministic loop (1D actuation)
    }

    private double Clamp(double proposed, IBlockContext ctx)
    {
        var baseline = AsDouble(ctx.Commanded(BoundOutput)) ?? ctx.Param("baseline", 21);
        var band = Math.Max(0.1, ctx.Param("band", 1.5));
        return Math.Clamp(proposed, baseline - band, baseline + band);
    }
}
