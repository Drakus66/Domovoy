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

    public sealed record Result(byte[] Artifact, double Rmse, int SampleCount);

    public Result? Train(IReadOnlyList<(DateTime Timestamp, double Value)> samples, int minSamples = 20)
    {
        if (samples.Count < minSamples) return null;

        var ml = new MLContext(seed: 0);
        var rows = samples.Select(s => new MlSample
        {
            Hour = (float)(s.Timestamp.Hour + s.Timestamp.Minute / 60.0),
            Dow = (float)(int)s.Timestamp.DayOfWeek,
            Value = (float)s.Value,
        }).ToList();

        var data = ml.Data.LoadFromEnumerable(rows);
        var pipeline = ml.Transforms
            .Concatenate("Features", nameof(MlSample.Hour), nameof(MlSample.Dow))
            .Append(ml.Regression.Trainers.Sdca(labelColumnName: nameof(MlSample.Value), featureColumnName: "Features"));

        var model = pipeline.Fit(data);
        var metrics = ml.Regression.Evaluate(model.Transform(data), labelColumnName: nameof(MlSample.Value));

        using var stream = new MemoryStream();
        ml.Model.Save(model, data.Schema, stream);
        return new Result(stream.ToArray(), metrics.RootMeanSquaredError, rows.Count);
    }
}
