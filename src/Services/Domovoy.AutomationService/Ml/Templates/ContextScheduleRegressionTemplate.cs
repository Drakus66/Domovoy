using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Microsoft.ML;
using Microsoft.ML.Data;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Number → regression on time + <b>home mode</b> (roadmap Epic 2B/2I) — the context-aware sibling of
/// <see cref="ScheduleRegressionTemplate"/>. It one-hot-encodes the mode and concatenates it with the
/// time-of-day/day-of-week features, so a setpoint the model proposes can differ by Home/Away/Night. It
/// competes head-to-head with the time-only template on honest holdout MAE (roadmap Epic 2I model selection):
/// it is registered/served only when the extra feature genuinely lowers holdout error, so mode noise can never
/// make things worse. Served with train/serve parity by <see cref="MlModelService"/>, which conditions on the
/// <i>current</i> home mode at inference time.
/// </summary>
public sealed class ContextScheduleRegressionTemplate : IModelTemplate
{
    public const string NoMode = "none";

    public string Kind => MlModelKinds.ScheduleRegressionContext;
    public CapabilityKind Target => CapabilityKind.Number;
    public string Metric => "MAE";
    public bool LowerIsBetter => true;
    public string Algorithm => "Sdca";
    public string Features => "time+mode";

    public TemplateResult? Train(IReadOnlyList<LabeledSample> samples, int minSamples)
    {
        if (samples.Count < minSamples) return null;

        // Needs at least two distinct modes to add anything over the time-only template — otherwise decline and
        // let the plain schedule model win selection (no spurious "context" model on single-mode history).
        if (samples.Select(s => s.Mode ?? NoMode).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            return null;

        var ml = new MLContext(seed: 0);
        var rows = samples.Select(ToRow).ToList();
        var data = ml.Data.LoadFromEnumerable(rows);

        var model = BuildPipeline(ml).Fit(data);
        var metrics = ml.Regression.Evaluate(model.Transform(data), labelColumnName: nameof(ContextSample.Value));

        var (mae, holdoutCount) = Backtest(rows, minSamples);

        using var stream = new MemoryStream();
        ml.Model.Save(model, data.Schema, stream);
        return new TemplateResult(stream.ToArray(), metrics.RootMeanSquaredError, rows.Count, mae, holdoutCount);
    }

    private static (double Mae, int Count) Backtest(IReadOnlyList<ContextSample> rows, int minSamples)
    {
        var ordered = rows.OrderBy(r => r.Ticks).ToList();
        var holdoutCount = (int)Math.Round(ordered.Count * 0.2);
        var trainCount = ordered.Count - holdoutCount;
        if (holdoutCount == 0 || trainCount < minSamples) return (0, 0);

        var ml = new MLContext(seed: 0);
        var model = BuildPipeline(ml).Fit(ml.Data.LoadFromEnumerable(ordered.Take(trainCount)));
        var scored = ml.Regression.Evaluate(
            model.Transform(ml.Data.LoadFromEnumerable(ordered.Skip(trainCount))), labelColumnName: nameof(ContextSample.Value));
        return (scored.MeanAbsoluteError, holdoutCount);
    }

    private static ContextSample ToRow(LabeledSample s) => new()
    {
        Hour = (float)(s.Timestamp.Hour + s.Timestamp.Minute / 60.0),
        Dow = (float)(int)s.Timestamp.DayOfWeek,
        Mode = string.IsNullOrWhiteSpace(s.Mode) ? NoMode : s.Mode!,
        Value = (float)s.Value,
        Ticks = s.Timestamp.Ticks,
    };

    private static IEstimator<ITransformer> BuildPipeline(MLContext ml) =>
        ml.Transforms.Categorical.OneHotEncoding("ModeEncoded", nameof(ContextSample.Mode))
            .Append(ml.Transforms.Concatenate("Features", nameof(ContextSample.Hour), nameof(ContextSample.Dow), "ModeEncoded"))
            .Append(ml.Regression.Trainers.Sdca(labelColumnName: nameof(ContextSample.Value), featureColumnName: "Features"));

    /// <summary>Training/inference row for the context regression: time + home mode → value.</summary>
    public sealed class ContextSample
    {
        public float Hour { get; set; }
        public float Dow { get; set; }
        public string Mode { get; set; } = NoMode;
        public float Value { get; set; }
        public long Ticks { get; set; } // for the chronological holdout only (not a feature)
    }

    /// <summary>Regression output.</summary>
    public sealed class ContextPrediction
    {
        [ColumnName("Score")]
        public float Value { get; set; }
    }
}
