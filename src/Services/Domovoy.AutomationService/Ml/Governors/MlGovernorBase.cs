using System.Globalization;

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Shared machinery for ML governor blocks (roadmap Epic 2I) — the domain-agnostic core extracted from the
/// 2B thermostat. A governor does <b>not</b> drive an actuator: it proposes a value (from a loaded model) to a
/// deterministic loop by commanding that loop's writable output, which then executes under the safety floor
/// ("the model proposes, the loop executes, the floor clamps").
///
/// <para>The base owns the parts that are identical for every value-type: the authority-stage machine
/// (0 Shadow / 1 Bounded / 2 Full), the rolling drift monitor with auto-demote to Shadow, the safe default
/// when no model is loaded, and the observational outputs (<see cref="ProposedSetpoint"/>,
/// <see cref="EffectiveStage"/>, <see cref="Drift"/>) feeding the scorecard/history. Subclasses supply only
/// the value-type specifics: how the Bounded stage clamps the proposal (<see cref="ApplyBoundedClamp"/>) and,
/// optionally, what "disagreement" means for the drift signal (<see cref="Disagreement"/>).</para>
///
/// <para>v1 is shaped for numeric setpoints (the floor clamps every active stage). Toggle/Selector governors
/// for Boolean/Enum targets build on the same stage+drift core in later phases.</para>
/// </summary>
public abstract class MlGovernorBase : IBlock
{
    /// <summary>Raw model proposal, always emitted (scorecard / Shadow visibility).</summary>
    public const string ProposedSetpoint = "proposed_setpoint";
    /// <summary>Effective authority stage after any auto-demote (0/1/2).</summary>
    public const string EffectiveStage = "ml_effective_stage";
    /// <summary>Rolling mean disagreement over the drift window.</summary>
    public const string Drift = "ml_drift";

    protected const int Shadow = 0;
    protected const int Bounded = 1;
    protected const int Full = 2;

    private readonly Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> _predict;
    private readonly string _measuredInput;

    /// <summary>The writable capability this governor commands on the deterministic loop.</summary>
    protected string BoundOutput { get; }
    protected double FloorMin { get; }
    protected double FloorMax { get; }

    // Rolling drift window: (tick time, per-tick disagreement).
    private readonly Queue<(DateTimeOffset At, double Error)> _errors = new();

    /// <param name="predict">Value prediction for a time + model-scope chain, or null when no model is loaded.</param>
    /// <param name="floorMin">Safety floor minimum, clamped on every active stage.</param>
    /// <param name="floorMax">Safety floor maximum, clamped on every active stage.</param>
    /// <param name="measuredInput">Input port carrying the measured signal, for the drift monitor.</param>
    /// <param name="boundOutput">Writable output capability the governor commands when active.</param>
    protected MlGovernorBase(
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> predict,
        double floorMin, double floorMax, string measuredInput, string boundOutput)
    {
        _predict = predict;
        FloorMin = floorMin;
        FloorMax = floorMax;
        _measuredInput = measuredInput;
        BoundOutput = boundOutput;
    }

    public void Tick(IBlockContext ctx)
    {
        var configuredStage = (int)Math.Round(Math.Clamp(ctx.Param("stage", Shadow), Shadow, Full));

        // No model yet → safe default: stay in Shadow, drive nothing, surface the inactive stage. The model is
        // resolved along the instance's zone → zone_kind → global scope chain (Epic 2I).
        var raw = _predict(ctx.Now, BuildScopeChain(ctx));
        if (raw is null)
        {
            ctx.Emit(EffectiveStage, (double)Shadow);
            return;
        }

        var proposed = Math.Round(raw.Value, 2);
        ctx.Emit(ProposedSetpoint, proposed);

        // Drift monitor: rolling mean disagreement between the model and reality. A persistently large gap
        // means the model is stale / the environment changed → auto-demote to Shadow (safe default).
        var drift = UpdateDrift(ctx, proposed);
        if (drift is not null) ctx.Emit(Drift, Math.Round(drift.Value, 3));

        var driftThreshold = Math.Max(0.5, ctx.Param("driftThreshold", 3));
        var effectiveStage = drift is { } d && d > driftThreshold ? Shadow : configuredStage;
        ctx.Emit(EffectiveStage, (double)effectiveStage);

        if (effectiveStage == Shadow)
        {
            if (configuredStage != Shadow && drift is { } dd && dd > driftThreshold)
                ctx.Log($"ML drift {dd:0.##} > {driftThreshold:0.##} — auto-demoted to Shadow");
            return; // Shadow: no bound emit → runtime publishes no command.
        }

        // Active stages: Bounded clamps via the subclass, Full passes through, then the safety floor clamps.
        var target = effectiveStage == Bounded ? ApplyBoundedClamp(proposed, ctx) : proposed;
        target = Math.Round(Math.Clamp(target, FloorMin, FloorMax), 2);

        ctx.Emit(BoundOutput, target); // bound → commands the deterministic loop (1D actuation)
    }

    /// <summary>
    /// The instance's model-scope fallback chain, most specific first (Epic 2I): zone → zone_kind → global.
    /// The predictor returns the first scope that has a loaded model, so a bedroom uses its own model if
    /// trained, else the shared "living rooms" model, else the house-wide one.
    /// </summary>
    private static IReadOnlyList<ModelScope> BuildScopeChain(IBlockContext ctx)
    {
        var chain = new List<ModelScope>(3);
        if (!string.IsNullOrEmpty(ctx.ZoneId)) chain.Add(ModelScope.Zone(ctx.ZoneId));
        if (!string.IsNullOrEmpty(ctx.ZoneKind)) chain.Add(ModelScope.ZoneKind(ctx.ZoneKind));
        chain.Add(ModelScope.Global);
        return chain;
    }

    /// <summary>Bounded-Active clamp of the raw proposal (e.g. baseline±band). Floor clamp is applied after.</summary>
    protected abstract double ApplyBoundedClamp(double proposed, IBlockContext ctx);

    /// <summary>Per-tick disagreement fed into the rolling drift mean. Numeric default = absolute error.</summary>
    protected virtual double Disagreement(double proposed, double measured) => Math.Abs(proposed - measured);

    // Push the new disagreement, evict samples older than the window, return the window mean (null until seeded).
    private double? UpdateDrift(IBlockContext ctx, double proposed)
    {
        var measured = ctx.ReadNumber(_measuredInput);
        if (measured is null) return null;

        var windowMin = Math.Max(1, ctx.Param("driftWindowMin", 60));
        _errors.Enqueue((ctx.Now, Disagreement(proposed, measured.Value)));
        while (_errors.Count > 0 && (ctx.Now - _errors.Peek().At).TotalMinutes > windowMin)
            _errors.Dequeue();

        return _errors.Count == 0 ? null : _errors.Average(e => e.Error);
    }

    protected static double? AsDouble(object? v) => v switch
    {
        null => null,
        double d => d,
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };
}
