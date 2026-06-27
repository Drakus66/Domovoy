using System.Globalization;

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// ML setpoint governor — the flagship ML-thermostat use-case (roadmap Epic 2B). It does <b>not</b> drive
/// an actuator: it only proposes a temperature setpoint (from the learned schedule model, <see cref="MlModelService"/>)
/// to the deterministic loop (a plain <c>thermostat</c> block) by commanding that loop's
/// <c>temperature_setpoint</c> — so the deterministic controller still executes under the safety floor
/// (layered model, principle 1; "the model proposes, the loop executes, the floor clamps").
///
/// <para><b>Authority stages</b> (numeric param <c>stage</c>): 0 = Shadow, 1 = Bounded-Active, 2 = Full.</para>
/// <list type="bullet">
/// <item>Shadow — predicts and emits <c>proposed_setpoint</c> for the scorecard, but <b>does not emit the
/// bound <c>temperature_setpoint</c></b>, so the runtime publishes no command (mirrors Shadow rules, 1F).</item>
/// <item>Bounded-Active — applies the proposal clamped to a narrow band around the trusted baseline.</item>
/// <item>Full — applies the proposal clamped only by the safety floor.</item>
/// </list>
/// The safety floor (<c>min</c>/<c>max</c>) clamps on every stage. When the model is missing/stale or the
/// rolling prediction error drifts past <c>driftThreshold</c>, the block auto-demotes to Shadow (safe default).
/// </summary>
public sealed class MlThermostatType : IBlockType
{
    private readonly MlModelService _models;
    private readonly double _floorMin;
    private readonly double _floorMax;

    public MlThermostatType(MlModelService models, double floorMin, double floorMax)
    {
        _models = models;
        _floorMin = floorMin;
        _floorMax = floorMax;

        Outputs = new[]
        {
            // Bound output (drives the deterministic loop's setpoint) — emitted only when active.
            WellKnownCapabilities.TemperatureSetpoint(min: floorMin, max: floorMax, step: 0.5),
            // Observational outputs (always emitted) — feed the scorecard / Shadow comparison + history (P0-5).
            WellKnownCapabilities.Number(MlThermostatBlock.ProposedSetpoint, "°C", floorMin, floorMax, step: 0.1),
            WellKnownCapabilities.Number(MlThermostatBlock.EffectiveStage, null, 0, 2, step: 1),
            WellKnownCapabilities.Number(MlThermostatBlock.Drift, "°C", 0, null, step: 0.01),
        };
    }

    public string TypeId => "ml_thermostat";
    public string Title => "ML thermostat (setpoint governor)";
    public string Description =>
        "Proposes a learned temperature setpoint to a deterministic thermostat loop, staged Shadow → Bounded → Full under the safety floor (Epic 2B).";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("temperature", CapabilityKind.Number, "Measured (ideally filtered) temperature — for the drift monitor"),
    };

    public IReadOnlyList<Capability> Outputs { get; }

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("stage", 0, null, 0, 2, "Authority stage: 0 = Shadow, 1 = Bounded-Active, 2 = Full"),
        new BlockParamSpec("baseline", 21, "°C", 5, 35, "Trusted baseline setpoint (Bounded band is centred here; commandable live)"),
        new BlockParamSpec("band", 1.5, "°C", 0.1, 5, "Bounded-Active: max deviation from baseline the ML may apply"),
        new BlockParamSpec("driftThreshold", 3, "°C", 0.5, 10, "Mean prediction error that auto-demotes to Shadow"),
        new BlockParamSpec("driftWindowMin", 60, "min", 5, 1440, "Rolling window for the drift mean"),
    };

    public IBlock Create() => new MlThermostatBlock(
        now => _models.TryPredict(now, out var v) ? v : null, _floorMin, _floorMax);
}

public sealed class MlThermostatBlock : IBlock
{
    /// <summary>Raw ML proposal, always emitted (scorecard / Shadow visibility).</summary>
    public const string ProposedSetpoint = "proposed_setpoint";
    /// <summary>Effective authority stage after any auto-demote (0/1/2).</summary>
    public const string EffectiveStage = "ml_effective_stage";
    /// <summary>Rolling mean |proposed − measured| over the drift window.</summary>
    public const string Drift = "ml_drift";

    private const int Shadow = 0;
    private const int Bounded = 1;
    private const int Full = 2;

    private readonly Func<DateTimeOffset, double?> _predict;
    private readonly double _floorMin;
    private readonly double _floorMax;

    // Rolling drift window: (tick time, |proposed − measured|).
    private readonly Queue<(DateTimeOffset At, double Error)> _errors = new();

    /// <param name="predict">Setpoint prediction for a given time, or null when no model is loaded.</param>
    public MlThermostatBlock(Func<DateTimeOffset, double?> predict, double floorMin, double floorMax)
    {
        _predict = predict;
        _floorMin = floorMin;
        _floorMax = floorMax;
    }

    public void Tick(IBlockContext ctx)
    {
        var configuredStage = (int)Math.Round(Math.Clamp(ctx.Param("stage", Shadow), Shadow, Full));
        var baseline = AsDouble(ctx.Commanded(CapabilityIds.TemperatureSetpoint)) ?? ctx.Param("baseline", 21);
        var band = Math.Max(0.1, ctx.Param("band", 1.5));

        // No model yet → safe default: stay in Shadow, drive nothing, surface the inactive stage.
        var raw = _predict(ctx.Now);
        if (raw is null)
        {
            ctx.Emit(EffectiveStage, (double)Shadow);
            return;
        }

        var proposed = Math.Round(raw.Value, 2);
        ctx.Emit(ProposedSetpoint, proposed);

        // Drift monitor: rolling mean error between the learned schedule and reality. A persistently large
        // gap means the model is stale / the environment changed → auto-demote to Shadow (safe default).
        var drift = UpdateDrift(ctx, proposed);
        if (drift is not null) ctx.Emit(Drift, Math.Round(drift.Value, 3));

        var driftThreshold = Math.Max(0.5, ctx.Param("driftThreshold", 3));
        var effectiveStage = drift is { } d && d > driftThreshold ? Shadow : configuredStage;
        ctx.Emit(EffectiveStage, (double)effectiveStage);

        if (effectiveStage == Shadow)
        {
            if (configuredStage != Shadow && drift is { } dd && dd > driftThreshold)
                ctx.Log($"ML drift {dd:0.##}°C > {driftThreshold:0.##}°C — auto-demoted to Shadow");
            return; // Shadow: no bound emit → runtime publishes no command.
        }

        // Active stages: clamp the proposal, then always clamp to the safety floor.
        var target = effectiveStage == Bounded
            ? Math.Clamp(proposed, baseline - band, baseline + band)
            : proposed;
        target = Math.Round(Math.Clamp(target, _floorMin, _floorMax), 2);

        ctx.Emit(CapabilityIds.TemperatureSetpoint, target); // bound → commands the deterministic loop (1D)
    }

    // Push the new error, evict samples older than the window, return the window mean (null until seeded).
    private double? UpdateDrift(IBlockContext ctx, double proposed)
    {
        var measured = ctx.ReadNumber("temperature");
        if (measured is null) return null;

        var windowMin = Math.Max(1, ctx.Param("driftWindowMin", 60));
        _errors.Enqueue((ctx.Now, Math.Abs(proposed - measured.Value)));
        while (_errors.Count > 0 && (ctx.Now - _errors.Peek().At).TotalMinutes > windowMin)
            _errors.Dequeue();

        return _errors.Count == 0 ? null : _errors.Average(e => e.Error);
    }

    private static double? AsDouble(object? v) => v switch
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
