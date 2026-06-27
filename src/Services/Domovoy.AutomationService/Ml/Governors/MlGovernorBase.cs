using System.Globalization;

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Shared machinery for ML governor blocks (roadmap Epic 2I) — the domain-agnostic core extracted from the
/// 2B thermostat. A governor does <b>not</b> drive an actuator: it proposes a value (from a loaded model) to a
/// deterministic loop by commanding that loop's writable output, which then executes under its own safety
/// limits ("the model proposes, the loop executes, the floor clamps").
///
/// <para>The base owns the parts identical for every value-type: the authority-stage machine (0 Shadow /
/// 1 Bounded / 2 Full), the rolling drift monitor with auto-demote to Shadow, the safe default when no model
/// is loaded, the model-scope chain (zone → zone_kind → global), and the observational outputs
/// (<see cref="Proposed"/>, <see cref="EffectiveStage"/>, <see cref="Drift"/>). Subclasses supply only the
/// value-type specifics: how an active stage turns the proposal into a bound command
/// (<see cref="EmitBound"/>) and, optionally, what "disagreement" means for the drift signal
/// (<see cref="Disagreement"/>) and its threshold scale (<see cref="DefaultDriftThreshold"/>).</para>
/// </summary>
public abstract class MlGovernorBase : IBlock
{
    /// <summary>Raw model proposal, always emitted (scorecard / Shadow visibility). For a setpoint a value, for a toggle a probability.</summary>
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

    // Rolling drift window: (tick time, per-tick disagreement).
    private readonly Queue<(DateTimeOffset At, double Error)> _errors = new();

    /// <param name="predict">Value prediction for a time + model-scope chain, or null when no model is loaded.</param>
    /// <param name="measuredInput">Input port carrying the measured signal, for the drift monitor.</param>
    /// <param name="boundOutput">Writable output capability the governor commands when active.</param>
    protected MlGovernorBase(
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, double?> predict, string measuredInput, string boundOutput)
    {
        _predict = predict;
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

        var proposed = Math.Round(raw.Value, 4);
        ctx.Emit(ProposedSetpoint, proposed);

        // Drift monitor: rolling mean disagreement between the model and reality. A persistently large gap
        // means the model is stale / the environment changed → auto-demote to Shadow (safe default).
        var drift = UpdateDrift(ctx, proposed);
        if (drift is not null) ctx.Emit(Drift, Math.Round(drift.Value, 4));

        var driftThreshold = Math.Max(0.0001, ctx.Param("driftThreshold", DefaultDriftThreshold));
        var effectiveStage = drift is { } d && d > driftThreshold ? Shadow : configuredStage;
        ctx.Emit(EffectiveStage, (double)effectiveStage);

        if (effectiveStage == Shadow)
        {
            if (configuredStage != Shadow && drift is { } dd && dd > driftThreshold)
                ctx.Log($"ML drift {dd:0.###} > {driftThreshold:0.###} — auto-demoted to Shadow");
            return; // Shadow: no bound emit → runtime publishes no command.
        }

        // Active stages: the subclass turns the proposal into a bound command for its value-type.
        EmitBound(ctx, proposed, bounded: effectiveStage == Bounded);
    }

    /// <summary>
    /// Turn the model proposal into a bound command on an active stage and emit it via
    /// <see cref="IBlockContext.Emit"/> on <see cref="BoundOutput"/>. <paramref name="bounded"/> is true for
    /// Bounded-Active (apply the stage's conservatism), false for Full.
    /// </summary>
    protected abstract void EmitBound(IBlockContext ctx, double proposed, bool bounded);

    /// <summary>Per-tick disagreement fed into the rolling drift mean. Numeric default = absolute error.</summary>
    protected virtual double Disagreement(IBlockContext ctx, double proposed, double measured) =>
        Math.Abs(proposed - measured);

    /// <summary>Default drift threshold when the instance doesn't set one (value-type scale).</summary>
    protected virtual double DefaultDriftThreshold => 3;

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

    // Push the new disagreement, evict samples older than the window, return the window mean (null until seeded).
    private double? UpdateDrift(IBlockContext ctx, double proposed)
    {
        var measured = ctx.ReadNumber(_measuredInput);
        if (measured is null) return null;

        var windowMin = Math.Max(1, ctx.Param("driftWindowMin", 60));
        _errors.Enqueue((ctx.Now, Disagreement(ctx, proposed, measured.Value)));
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
