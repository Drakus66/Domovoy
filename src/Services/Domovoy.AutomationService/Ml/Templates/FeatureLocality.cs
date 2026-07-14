// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// A candidate input feature for a multivariate model (roadmap Epic 2I, Phase 4): a capability, the zone it
/// lives in, and whether it is an ambient/global signal. Ambient features (time, home mode, outdoor weather)
/// influence every zone and carry no zone; zone-local sensors carry their zone id.
/// </summary>
public sealed record FeatureCandidate(string CapabilityId, string? ZoneId = null, bool Ambient = false);

/// <summary>Admissibility + weight of a feature for a model (roadmap Epic 2I, Phase 4).</summary>
public readonly record struct FeatureAdmission(bool Admissible, double Weight);

/// <summary>
/// Spatial feature-locality policy (roadmap Epic 2I, Phase 4) — the same zone chain that scopes a model also
/// scopes its <i>inputs</i>. For a model trained for a given scope it decides which candidate sensors may feed
/// it, so the model learns from spatially relevant signals and not from spurious cross-zone correlation:
/// <list type="bullet">
/// <item><b>Ambient</b> signals (time, mode, outdoor weather) feed every model.</item>
/// <item>Sensors in the model's <b>own zone</b> feed it at full weight.</item>
/// <item>Sensors in a <b>sibling zone of the same kind</b> feed it at a reduced weight (the living room nudges
/// the bedroom — minimal but real).</item>
/// <item>Sensors across a <b>zone-kind boundary</b> are excluded a priori — the house never feeds the
/// greenhouse and vice versa, even if an in-sample correlation appears. This is the hard wall.</item>
/// </list>
/// A global-scoped model uses only ambient features, staying neutral across kinds.
/// </summary>
public static class FeatureLocality
{
    private static readonly FeatureAdmission Excluded = new(false, 0);

    /// <summary>
    /// Whether <paramref name="candidate"/> may feed a model trained for <paramref name="modelScope"/>, and at
    /// what weight. <paramref name="kindOf"/> maps a zone id to its kind (e.g. <c>ZoneCache.KindOf</c>);
    /// <paramref name="sameKindWeight"/> is the down-weight for same-kind sibling zones.
    /// </summary>
    public static FeatureAdmission Evaluate(
        ModelScope modelScope, FeatureCandidate candidate, Func<string?, string?> kindOf, double sameKindWeight = 0.5)
    {
        // Ambient (time / mode / weather) influences every zone — always admissible at full weight.
        if (candidate.Ambient) return new FeatureAdmission(true, 1.0);

        var modelKind = ScopeKind(modelScope, kindOf);
        if (modelKind is null) return Excluded; // global model → ambient features only

        var candidateKind = string.IsNullOrEmpty(candidate.ZoneId) ? null : kindOf(candidate.ZoneId);
        if (candidateKind is null) return Excluded; // unknown placement → don't risk it

        // Hard wall: a different zone kind never feeds this model (house ↔ greenhouse).
        if (!string.Equals(candidateKind, modelKind, StringComparison.OrdinalIgnoreCase)) return Excluded;

        // Same kind: own zone at full weight, sibling zone down-weighted.
        var ownZone = modelScope.Level == ModelScopeLevels.Zone
            && string.Equals(candidate.ZoneId, modelScope.Key, StringComparison.Ordinal);
        return new FeatureAdmission(true, ownZone ? 1.0 : Math.Clamp(sameKindWeight, 0, 1));
    }

    /// <summary>Keep only the admissible candidates for a model scope (with their weights).</summary>
    public static IReadOnlyList<(FeatureCandidate Feature, double Weight)> Admissible(
        ModelScope modelScope, IEnumerable<FeatureCandidate> candidates, Func<string?, string?> kindOf, double sameKindWeight = 0.5)
    {
        var result = new List<(FeatureCandidate, double)>();
        foreach (var c in candidates)
        {
            var a = Evaluate(modelScope, c, kindOf, sameKindWeight);
            if (a.Admissible) result.Add((c, a.Weight));
        }
        return result;
    }

    // The zone kind a model scope is anchored to: the zone's kind for a zone scope, the kind itself for a
    // zone-kind scope, null for global.
    private static string? ScopeKind(ModelScope scope, Func<string?, string?> kindOf) => scope.Level switch
    {
        ModelScopeLevels.Zone => kindOf(scope.Key),
        ModelScopeLevels.ZoneKind => scope.Key,
        _ => null,
    };
}
