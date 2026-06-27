using Microsoft.ML;

namespace Domovoy.AutomationService.Ml;

/// <summary>
/// Trains the v1 schedule-regression model with ML.NET (roadmap Epic 2A) — pure and dependency-free so it
/// unit-tests without infrastructure. Features = hour-of-day + day-of-week; label = the target value; SDCA
/// linear regression. Returns the serialized model bytes + RMSE, or null if there is too little data.
/// </summary>
public sealed class MlTrainer
{
    public const string Algorithm = "Sdca";

    public sealed record Result(byte[] Artifact, double Rmse, int SampleCount, double HoldoutMae, int HoldoutCount);

    /// <summary>
    /// Train the schedule model. The artifact + in-sample RMSE come from fitting on <i>all</i> samples; the
    /// honest backtest signal (held-out MAE, Epic 2B) is computed by re-fitting on the chronologically older
    /// <c>1 − holdoutFraction</c> and scoring the most recent <c>holdoutFraction</c> it never saw. The shipped
    /// artifact is the full-data fit (more data = better serving); the holdout only measures generalization.
    /// </summary>
    public Result? Train(IReadOnlyList<(DateTime Timestamp, double Value)> samples, int minSamples = 20, double holdoutFraction = 0.2)
    {
        if (samples.Count < minSamples) return null;

        var ml = new MLContext(seed: 0);
        var rows = samples.Select(ToRow).ToList();

        var data = ml.Data.LoadFromEnumerable(rows);
        var model = BuildPipeline(ml).Fit(data);
        var metrics = ml.Regression.Evaluate(model.Transform(data), labelColumnName: nameof(MlSample.Value));

        var (holdoutMae, holdoutCount) = Backtest(samples, minSamples, holdoutFraction);

        using var stream = new MemoryStream();
        ml.Model.Save(model, data.Schema, stream);
        return new Result(stream.ToArray(), metrics.RootMeanSquaredError, rows.Count, holdoutMae, holdoutCount);
    }

    // Chronological holdout: fit on the older split, measure MAE on the most recent unseen split.
    private static (double Mae, int Count) Backtest(
        IReadOnlyList<(DateTime Timestamp, double Value)> samples, int minSamples, double holdoutFraction)
    {
        var ordered = samples.OrderBy(s => s.Timestamp).ToList();
        var holdoutCount = (int)Math.Round(ordered.Count * Math.Clamp(holdoutFraction, 0, 0.9));
        var trainCount = ordered.Count - holdoutCount;
        if (holdoutCount == 0 || trainCount < minSamples) return (0, 0); // too little data to back-test

        var ml = new MLContext(seed: 0);
        var trainRows = ordered.Take(trainCount).Select(ToRow).ToList();
        var holdoutRows = ordered.Skip(trainCount).Select(ToRow).ToList();

        var model = BuildPipeline(ml).Fit(ml.Data.LoadFromEnumerable(trainRows));
        var scored = ml.Regression.Evaluate(
            model.Transform(ml.Data.LoadFromEnumerable(holdoutRows)), labelColumnName: nameof(MlSample.Value));
        return (scored.MeanAbsoluteError, holdoutCount);
    }

    private static MlSample ToRow((DateTime Timestamp, double Value) s) => new()
    {
        Hour = (float)(s.Timestamp.Hour + s.Timestamp.Minute / 60.0),
        Dow = (float)(int)s.Timestamp.DayOfWeek,
        Value = (float)s.Value,
    };

    private static IEstimator<ITransformer> BuildPipeline(MLContext ml) =>
        ml.Transforms
            .Concatenate("Features", nameof(MlSample.Hour), nameof(MlSample.Dow))
            .Append(ml.Regression.Trainers.Sdca(labelColumnName: nameof(MlSample.Value), featureColumnName: "Features"));
}
