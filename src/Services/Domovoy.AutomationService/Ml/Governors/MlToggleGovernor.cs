using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Boolean → toggle governor (roadmap Epic 2I, Phase 2) — the on/off sibling of the setpoint governor. The
/// model proposes a probability the device should be on (a learned on/off schedule); the governor thresholds
/// it into a boolean command for a deterministic loop. Bounded-Active widens the threshold into a hysteresis
/// band (a stronger probability is needed to flip than to hold) and a minimum dwell time damps chatter; Full
/// uses the bare threshold. The drift signal is the rolling rate at which the predicted state disagrees with
/// the measured one, so a stale schedule auto-demotes to Shadow like the setpoint governor.
/// </summary>
public sealed class MlToggleGovernor : MlGovernorBase
{
    private bool _lastOn;
    private DateTimeOffset? _lastFlip;

    public MlToggleGovernor(
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> predict, string measuredInput, string boundOutput)
        : base(predict, measuredInput, boundOutput)
    {
    }

    // Disagreement rates live in [0,1]; default the auto-demote threshold to "wrong more than half the time".
    protected override double DefaultDriftThreshold => 0.5;

    protected override void EmitBound(IBlockContext ctx, double probability, bool bounded)
    {
        var threshold = Math.Clamp(ctx.Param("probThreshold", 0.5), 0, 1);
        var margin = bounded ? Math.Max(0, ctx.Param("boundedMargin", 0.2)) : 0;

        // Hysteresis: once on, stay on until clearly below; once off, turn on only when clearly above.
        var desired = _lastOn ? probability > threshold - margin : probability >= threshold + margin;

        if (desired != _lastOn)
        {
            var dwellMin = Math.Max(0, ctx.Param("minDwellMin", 10));
            var heldLongEnough = _lastFlip is null || (ctx.Now - _lastFlip.Value).TotalMinutes >= dwellMin;
            if (heldLongEnough)
            {
                _lastOn = desired;
                _lastFlip = ctx.Now;
            }
        }

        ctx.Emit(BoundOutput, _lastOn); // bound → commands the deterministic loop (1D actuation)
    }

    // Drift = predicted class vs measured class (0/1), so the rolling mean is the disagreement rate.
    protected override double Disagreement(IBlockContext ctx, double probability, double measured)
    {
        var threshold = Math.Clamp(ctx.Param("probThreshold", 0.5), 0, 1);
        var predictedOn = probability >= threshold ? 1.0 : 0.0;
        var measuredOn = measured >= 0.5 ? 1.0 : 0.0;
        return Math.Abs(predictedOn - measuredOn);
    }
}
