using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// A labeled training row at a point in time (roadmap Epic 2I). <see cref="Value"/> carries numeric and
/// boolean (0/1) targets so they train the way telemetry does; <see cref="Class"/> carries the string label
/// for enum/categorical targets (Phase 3). A row uses one or the other depending on the template.
/// </summary>
public readonly record struct LabeledSample(DateTime Timestamp, double Value, string? Class = null);

/// <summary>Outcome of training a template: the serialized artifact plus its honest holdout score.</summary>
/// <param name="Artifact">Serialized ML.NET model bytes.</param>
/// <param name="InSampleError">Fit error on all samples (provenance; e.g. RMSE for regression).</param>
/// <param name="SampleCount">Number of training samples.</param>
/// <param name="HoldoutScore">Score on the most-recent chronological holdout, in this template's <see cref="IModelTemplate.Metric"/>.</param>
/// <param name="HoldoutCount">Number of held-out samples the score was computed on.</param>
public sealed record TemplateResult(
    byte[] Artifact, double InSampleError, int SampleCount, double HoldoutScore, int HoldoutCount);

/// <summary>
/// A pluggable model template (roadmap Epic 2I) — the unit that turns a labeled time-series into a trained
/// artifact. A template is one cell of the <c>{ targetKind, featureSet, mlTask }</c> grid: it declares which
/// capability data-type it serves (<see cref="Target"/>) and which honest metric scores it
/// (<see cref="Metric"/> / <see cref="LowerIsBetter"/>), so the trainer can pick the best candidate per target
/// by holdout. The flagship v1 cell is <see cref="ScheduleRegressionTemplate"/> (Number → regression);
/// Boolean → binary and Enum → multiclass templates fill the rest of the grid in later phases.
/// </summary>
public interface IModelTemplate
{
    /// <summary>Model family id stored on <c>MlModel.Kind</c> (see <see cref="Domovoy.Contracts.Ml.MlModelKinds"/>).</summary>
    string Kind { get; }

    /// <summary>Capability value-type this template can model.</summary>
    CapabilityKind Target { get; }

    /// <summary>Holdout metric name (e.g. <c>MAE</c>, <c>AUC</c>, <c>MacroF1</c>) — for the scorecard and selection.</summary>
    string Metric { get; }

    /// <summary>True when a lower <see cref="TemplateResult.HoldoutScore"/> is better (e.g. MAE); false for AUC/F1.</summary>
    bool LowerIsBetter { get; }

    /// <summary>Training algorithm, for provenance (<c>MlModel.Algorithm</c>).</summary>
    string Algorithm { get; }

    /// <summary>Train the template, or null when there is too little data.</summary>
    TemplateResult? Train(IReadOnlyList<LabeledSample> samples, int minSamples);
}
