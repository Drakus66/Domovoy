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

    /// <summary>Monotonic version per (kind, target) — bumped on each retrain.</summary>
    public int Version { get; set; } = 1;

    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of training samples (provenance / cold-start signal).</summary>
    public int SampleCount { get; set; }

    /// <summary>Training error (root mean squared error) — for the approval/scorecard view (2C).</summary>
    public double Rmse { get; set; }

    /// <summary>Training algorithm, for provenance.</summary>
    public string? Algorithm { get; set; }
}

/// <summary>Well-known ML model families (open set).</summary>
public static class MlModelKinds
{
    /// <summary>Regression of a numeric target from time-of-day/day-of-week — a learned schedule.</summary>
    public const string ScheduleRegression = "schedule_regression";
}
