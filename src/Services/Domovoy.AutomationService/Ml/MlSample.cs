using Microsoft.ML.Data;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Training/inference row for the v1 schedule regression (roadmap Epic 2A): predict a numeric target
/// (e.g. a comfortable temperature setpoint) from time-of-day + day-of-week — a "learned schedule".
/// </summary>
public sealed class MlSample
{
    /// <summary>Hour of day with fractional minutes, 0..24.</summary>
    public float Hour { get; set; }

    /// <summary>Day of week, 0 (Sunday)..6.</summary>
    public float Dow { get; set; }

    /// <summary>Label — the observed/target value.</summary>
    public float Value { get; set; }
}

/// <summary>Regression output (ML.NET writes the prediction to the <c>Score</c> column).</summary>
public sealed class MlPrediction
{
    [ColumnName("Score")]
    public float Value { get; set; }
}
