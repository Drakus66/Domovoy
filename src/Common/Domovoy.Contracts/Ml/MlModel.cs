namespace Domovoy.Contracts.Ml;

/// <summary>
/// Metadata for a trained ML model (roadmap Epic 2A). The model is trained on the P0-5/1B feature store
/// (event-log + telemetry) and registered in the <c>ml_models</c> Mongo collection; the serialized model
/// artifact is stored alongside but fetched separately. An ML <b>block</b> (1H) loads the latest model and
/// serves predictions (e.g. a learned setpoint schedule). This is the substrate; the flagship ML-thermostat
/// use-case is Epic 2B.
/// </summary>
public class MlModel
{
    /// <summary>Stable model id (GUID string). Server-assigned on register.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Model family (see <see cref="MlModelKinds"/>).</summary>
    public string Kind { get; set; } = MlModelKinds.ScheduleRegression;

    /// <summary>Capability the model predicts (e.g. <c>temperature</c>).</summary>
    public string TargetCapability { get; set; } = string.Empty;

    /// <summary>
    /// Spatial scope the model serves (Epic 2I): a specific zone, a zone kind ("living rooms"), or global.
    /// Instances resolve a model along the zone→zone_kind→global chain; null/absent means global (back-compat).
    /// </summary>
    public ModelScope Scope { get; set; } = ModelScope.Global;

    /// <summary>Monotonic version per (kind, target, scope) — bumped on each retrain.</summary>
    public int Version { get; set; } = 1;

    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of training samples (provenance / cold-start signal).</summary>
    public int SampleCount { get; set; }

    /// <summary>Training error (root mean squared error) — for the approval/scorecard view (2C).</summary>
    public double Rmse { get; set; }

    /// <summary>
    /// Held-out backtest error (mean absolute error on the most recent window, excluded from training).
    /// The honest "prediction vs fact" signal for the approval scorecard (Epic 2B). 0 if not evaluated.
    /// Kept for back-compat; <see cref="HoldoutScore"/> generalizes it across template metrics (Epic 2I).
    /// </summary>
    public double HoldoutMae { get; set; }

    /// <summary>Number of held-out samples the backtest score was computed on (provenance).</summary>
    public int HoldoutSampleCount { get; set; }

    /// <summary>
    /// Honest holdout score in the template's own <see cref="Metric"/> (Epic 2I) — the signal both the
    /// approval scorecard and the model-selection / zone auto-promotion compare on. For regression equals
    /// <see cref="HoldoutMae"/>; classification templates report AUC / macro-F1 here.
    /// </summary>
    public double HoldoutScore { get; set; }

    /// <summary>Name of the holdout metric (<c>MAE</c>, <c>AUC</c>, <c>MacroF1</c>) — disambiguates <see cref="HoldoutScore"/>.</summary>
    public string Metric { get; set; } = "MAE";

    /// <summary>Feature set the model was trained on (Epic 2I), e.g. <c>time</c> or <c>time+mode+occupancy</c>.</summary>
    public string Features { get; set; } = "time";

    /// <summary>Training algorithm, for provenance.</summary>
    public string? Algorithm { get; set; }
}

/// <summary>Well-known ML model families (open set).</summary>
public static class MlModelKinds
{
    /// <summary>Regression of a numeric target from time-of-day/day-of-week — a learned schedule.</summary>
    public const string ScheduleRegression = "schedule_regression";

    /// <summary>Binary classification of a boolean target from time features — a learned on/off schedule (Epic 2I).</summary>
    public const string ScheduleBinary = "schedule_binary";

    /// <summary>Multiclass classification of an enum target from time features — a learned mode/level schedule (Epic 2I).</summary>
    public const string ScheduleMulticlass = "schedule_multiclass";
}
