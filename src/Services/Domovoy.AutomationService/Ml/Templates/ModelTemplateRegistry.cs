using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Registry of available model templates (roadmap Epic 2I). The trainer asks it for the templates applicable
/// to a target's <see cref="CapabilityKind"/>, trains each candidate, and keeps the best by honest holdout —
/// "models are selected from templates by the data", not hand-written per quantity. v1 holds the single
/// Number → regression cell; Boolean → binary and Enum → multiclass register here as later phases add them.
/// </summary>
public sealed class ModelTemplateRegistry
{
    private readonly IReadOnlyList<IModelTemplate> _templates;

    public ModelTemplateRegistry(IEnumerable<IModelTemplate> templates) => _templates = templates.ToList();

    /// <summary>Templates that can model a target of the given value-type.</summary>
    public IReadOnlyList<IModelTemplate> ForTarget(CapabilityKind kind) =>
        _templates.Where(t => t.Target == kind).ToList();

    /// <summary>Look up a template by its model-family id (<c>MlModel.Kind</c>).</summary>
    public IModelTemplate? ByKind(string kind) =>
        _templates.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pick the better of two holdout scores under a template's orientation (lower-is-better or not).</summary>
    public static bool IsBetter(IModelTemplate template, double candidate, double incumbent) =>
        template.LowerIsBetter ? candidate < incumbent : candidate > incumbent;

    /// <summary>
    /// Auto-promotion gate (roadmap Epic 2I): does a per-zone <paramref name="candidate"/> beat its
    /// <paramref name="fallback"/> (zone_kind/global) by at least <paramref name="margin"/> in the template's
    /// metric? Until it does, the zone stays on the shared model — a zone splinters into its own model only
    /// when its behaviour genuinely diverges, not on noise.
    /// </summary>
    public static bool ShouldPromote(IModelTemplate template, double candidate, double fallback, double margin) =>
        template.LowerIsBetter ? candidate <= fallback - margin : candidate >= fallback + margin;
}
