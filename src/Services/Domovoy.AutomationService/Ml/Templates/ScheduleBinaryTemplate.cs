using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Microsoft.ML;
using Microsoft.ML.Data;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Boolean → binary-classification template (roadmap Epic 2I, Phase 2): a learned on/off schedule (time-of-day
/// + day-of-week → probability the device is on), the basis of the ML toggle governor. Pure and
/// infrastructure-free so it unit-tests like the regression template. ML.NET L-BFGS logistic regression
/// (calibrated probability); honest holdout scored by AUC on the most-recent chronological slice.
/// </summary>
public sealed class ScheduleBinaryTemplate : IModelTemplate
{
    public const string Algo = "LbfgsLogisticRegression";

    public string Kind => MlModelKinds.ScheduleBinary;
    public CapabilityKind Target => CapabilityKind.Boolean;
    public string Metric => "AUC";
    public bool LowerIsBetter => false; // higher AUC is better
    public string Algorithm => Algo;

    public TemplateResult? Train(IReadOnlyList<LabeledSample> samples, int minSamples)
    {
        if (samples.Count < minSamples) return null;

        // A usable classifier needs both classes present; an all-on / all-off history can't be learned.
        var rows = samples.Select(ToRow).ToList();
        if (rows.All(r => r.Label) || rows.All(r => !r.Label)) return null;

        var ml = new MLContext(seed: 0);
        var data = ml.Data.LoadFromEnumerable(rows);
        var model = BuildPipeline(ml).Fit(data);
        var inSample = ml.BinaryClassification.Evaluate(model.Transform(data), labelColumnName: nameof(BinaryRow.Label));

        var (auc, holdoutCount) = Backtest(rows, minSamples);

        using var stream = new MemoryStream();
        ml.Model.Save(model, data.Schema, stream);
        // InSampleError carries log-loss for provenance; HoldoutScore is the honest AUC the selection compares.
        return new TemplateResult(stream.ToArray(), inSample.LogLoss, rows.Count, auc, holdoutCount);
    }

    // Chronological holdout: fit on the older split, score AUC on the most recent unseen split.
    private static (double Auc, int Count) Backtest(IReadOnlyList<BinaryRow> rows, int minSamples)
    {
        var ordered = rows.OrderBy(r => r.Timestamp).ToList();
        var holdout = (int)Math.Round(ordered.Count * 0.2);
        var trainCount = ordered.Count - holdout;
        if (holdout == 0 || trainCount < minSamples) return (0, 0);

        var train = ordered.Take(trainCount).ToList();
        var test = ordered.Skip(trainCount).ToList();
        // AUC is undefined unless both classes appear in each split.
        if (train.All(r => r.Label) || train.All(r => !r.Label) || test.All(r => r.Label) || test.All(r => !r.Label))
            return (0, 0);

        var ml = new MLContext(seed: 0);
        var model = BuildPipeline(ml).Fit(ml.Data.LoadFromEnumerable(train));
        var scored = ml.BinaryClassification.Evaluate(
            model.Transform(ml.Data.LoadFromEnumerable(test)), labelColumnName: nameof(BinaryRow.Label));
        return (scored.AreaUnderRocCurve, holdout);
    }

    private static BinaryRow ToRow(LabeledSample s) => new()
    {
        Timestamp = s.Timestamp,
        Hour = (float)(s.Timestamp.Hour + s.Timestamp.Minute / 60.0),
        Dow = (float)(int)s.Timestamp.DayOfWeek,
        Label = s.Value >= 0.5,
    };

    private static IEstimator<ITransformer> BuildPipeline(MLContext ml) =>
        ml.Transforms
            .Concatenate("Features", nameof(BinaryRow.Hour), nameof(BinaryRow.Dow))
            .Append(ml.BinaryClassification.Trainers.LbfgsLogisticRegression(
                labelColumnName: nameof(BinaryRow.Label), featureColumnName: "Features"));

    private sealed class BinaryRow
    {
        public DateTime Timestamp { get; set; }
        public float Hour { get; set; }
        public float Dow { get; set; }
        public bool Label { get; set; }
    }
}

/// <summary>Binary-classification inference row (time features) and its probability output (Epic 2I).</summary>
public sealed class MlBinarySample
{
    public float Hour { get; set; }
    public float Dow { get; set; }
    public bool Label { get; set; }
}

public sealed class MlBinaryPrediction
{
    public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
}
