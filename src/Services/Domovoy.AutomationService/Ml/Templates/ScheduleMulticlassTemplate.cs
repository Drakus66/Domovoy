using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Microsoft.ML;
using Microsoft.ML.Data;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Enum → multiclass-classification template (roadmap Epic 2I, Phase 3): a learned mode/level schedule
/// (time-of-day + day-of-week → which enum value, e.g. an HVAC mode or a fan level), the basis of the ML
/// selector governor. ML.NET SDCA maximum-entropy over string class labels; honest holdout scored by macro
/// accuracy on the most-recent chronological slice. Pure and infrastructure-free.
/// </summary>
public sealed class ScheduleMulticlassTemplate : IModelTemplate
{
    public const string Algo = "SdcaMaximumEntropy";

    public string Kind => MlModelKinds.ScheduleMulticlass;
    public CapabilityKind Target => CapabilityKind.Enum;
    public string Metric => "MacroAccuracy";
    public bool LowerIsBetter => false; // higher accuracy is better
    public string Algorithm => Algo;

    public TemplateResult? Train(IReadOnlyList<LabeledSample> samples, int minSamples)
    {
        var rows = samples.Where(s => !string.IsNullOrEmpty(s.Class)).Select(ToRow).ToList();
        if (rows.Count < minSamples) return null;

        // A classifier needs at least two distinct classes to learn a boundary.
        if (rows.Select(r => r.Label).Distinct(StringComparer.Ordinal).Count() < 2) return null;

        var ml = new MLContext(seed: 0);
        var data = ml.Data.LoadFromEnumerable(rows);
        var model = BuildPipeline(ml).Fit(data);
        var inSample = ml.MulticlassClassification.Evaluate(model.Transform(data), labelColumnName: "Label");

        var (macroAcc, holdoutCount) = Backtest(rows, minSamples);

        using var stream = new MemoryStream();
        ml.Model.Save(model, data.Schema, stream);
        // InSampleError carries log-loss for provenance; HoldoutScore is the honest macro accuracy.
        return new TemplateResult(stream.ToArray(), inSample.LogLoss, rows.Count, macroAcc, holdoutCount);
    }

    // Chronological holdout: fit on the older split, score macro accuracy on the most recent unseen split.
    private static (double MacroAccuracy, int Count) Backtest(IReadOnlyList<MulticlassRow> rows, int minSamples)
    {
        var ordered = rows.OrderBy(r => r.Timestamp).ToList();
        var holdout = (int)Math.Round(ordered.Count * 0.2);
        var trainCount = ordered.Count - holdout;
        if (holdout == 0 || trainCount < minSamples) return (0, 0);

        var train = ordered.Take(trainCount).ToList();
        var test = ordered.Skip(trainCount).ToList();
        // Every holdout class must be seen in training, else the key-mapping can't score it.
        var trainClasses = train.Select(r => r.Label).ToHashSet(StringComparer.Ordinal);
        if (train.Select(r => r.Label).Distinct(StringComparer.Ordinal).Count() < 2) return (0, 0);
        if (test.Any(r => !trainClasses.Contains(r.Label))) test = test.Where(r => trainClasses.Contains(r.Label)).ToList();
        if (test.Count == 0) return (0, 0);

        var ml = new MLContext(seed: 0);
        var model = BuildPipeline(ml).Fit(ml.Data.LoadFromEnumerable(train));
        var scored = ml.MulticlassClassification.Evaluate(
            model.Transform(ml.Data.LoadFromEnumerable(test)), labelColumnName: "Label");
        return (scored.MacroAccuracy, test.Count);
    }

    private static MulticlassRow ToRow(LabeledSample s) => new()
    {
        Timestamp = s.Timestamp,
        Hour = (float)(s.Timestamp.Hour + s.Timestamp.Minute / 60.0),
        Dow = (float)(int)s.Timestamp.DayOfWeek,
        Label = s.Class!,
    };

    private static IEstimator<ITransformer> BuildPipeline(MLContext ml) =>
        ml.Transforms.Conversion.MapValueToKey("Label", nameof(MulticlassRow.Label))
            .Append(ml.Transforms.Concatenate("Features", nameof(MulticlassRow.Hour), nameof(MulticlassRow.Dow)))
            .Append(ml.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
            .Append(ml.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

    private sealed class MulticlassRow
    {
        public DateTime Timestamp { get; set; }
        public float Hour { get; set; }
        public float Dow { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}

/// <summary>Multiclass inference row (time features) and its predicted class output (Epic 2I, Phase 3).</summary>
public sealed class MlMulticlassSample
{
    public float Hour { get; set; }
    public float Dow { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class MlMulticlassPrediction
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;
}
