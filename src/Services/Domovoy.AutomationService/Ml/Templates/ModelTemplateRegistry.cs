// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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

    /// <summary>
    /// Pick the better of two holdout scores under a template's orientation (lower-is-better or not). A null
    /// score means "could not be evaluated" (see <see cref="TemplateResult.HoldoutScore"/>) and is treated as
    /// no evidence, not as a good score: an evaluated candidate always beats an unevaluated incumbent, and an
    /// unevaluated candidate never displaces anything.
    /// </summary>
    public static bool IsBetter(IModelTemplate template, double? candidate, double? incumbent)
    {
        if (candidate is null) return false;              // no evidence never wins
        if (incumbent is null) return true;               // any evidence beats none
        return template.LowerIsBetter ? candidate < incumbent : candidate > incumbent;
    }

    /// <summary>
    /// Auto-promotion gate (roadmap Epic 2I): does a per-zone <paramref name="candidate"/> beat its
    /// <paramref name="fallback"/> (zone_kind/global) by at least <paramref name="margin"/> in the template's
    /// metric? Until it does, the zone stays on the shared model — a zone splinters into its own model only
    /// when its behaviour genuinely diverges, not on noise.
    ///
    /// <para>An unevaluated score on either side (null — see <see cref="TemplateResult.HoldoutScore"/>) means
    /// the comparison cannot be made, so the zone keeps the shared model. This is the case a zero used to
    /// mis-handle: a zone with barely enough samples to train but too few to hold out would produce
    /// <c>MAE = 0</c>, clear the margin against any real fallback and splinter off on pure noise.</para>
    /// </summary>
    public static bool ShouldPromote(IModelTemplate template, double? candidate, double? fallback, double margin)
    {
        if (candidate is null || fallback is null) return false; // no honest comparison → stay on the shared model
        return template.LowerIsBetter ? candidate <= fallback - margin : candidate >= fallback + margin;
    }
}
