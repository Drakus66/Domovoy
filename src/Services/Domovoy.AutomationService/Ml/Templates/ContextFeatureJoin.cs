// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Server-side context-join (roadmap Epic 2B/2I) — the prerequisite the roadmap named for enriching ML features
/// with context. It attaches the home mode in effect at each training row's timestamp by an <b>as-of</b> join
/// against the mode timeline (from the P0-5 event-log / 1G <c>mode_change</c> records): the last mode change at
/// or before the row. Home mode is an <b>ambient</b> feature (<see cref="FeatureLocality"/>) — it influences every
/// zone — so it feeds every model. Pure and unit-testable.
/// </summary>
public static class ContextFeatureJoin
{
    /// <summary>The mode in effect at <paramref name="at"/>: the last change at or before it, or null if none precedes it.</summary>
    public static string? ModeAt(DateTime at, IReadOnlyList<(DateTime At, string Mode)> timeline)
    {
        string? current = null;
        // Timeline is chronological; walk to the last entry not after `at` (linear — mode changes are rare).
        foreach (var (t, mode) in timeline)
        {
            if (t > at) break;
            current = mode;
        }
        return current;
    }

    /// <summary>Return the samples with <see cref="LabeledSample.Mode"/> filled from the mode timeline (as-of).</summary>
    public static List<LabeledSample> WithMode(
        IReadOnlyList<LabeledSample> samples, IReadOnlyList<(DateTime At, string Mode)> timeline)
    {
        if (timeline.Count == 0) return samples.ToList();
        var ordered = timeline.OrderBy(x => x.At).ToList();
        return samples.Select(s => s with { Mode = ModeAt(s.Timestamp, ordered) ?? s.Mode }).ToList();
    }

    /// <summary>The home mode as a feature candidate: <b>ambient</b> — it is set for the whole house, not a zone.</summary>
    public static readonly FeatureCandidate ModeCandidate = new(CapabilityIds.HomeMode, Ambient: true);

    /// <summary>
    /// Attach the mode only if the spatial policy (<see cref="FeatureLocality"/>) admits it as an input for a
    /// model of <paramref name="modelScope"/>. This is where the policy actually gates the pipeline: every
    /// feature a trained model gets passes through it, so "which signals may feed this scope" is enforced by the
    /// code that builds the features rather than only described. Mode is ambient, so today every scope admits it
    /// and the result equals <see cref="WithMode"/> — the point is that a zone-local input added later
    /// (Epic 2I Phase 4 multivariate models) is filtered by the same call, including the hard wall between zone
    /// kinds, instead of arriving unchecked.
    /// </summary>
    public static List<LabeledSample> WithAdmissibleMode(
        IReadOnlyList<LabeledSample> samples, IReadOnlyList<(DateTime At, string Mode)> timeline,
        ModelScope modelScope, Func<string?, string?> kindOf)
    {
        var admitted = FeatureLocality.Admissible(modelScope, new[] { ModeCandidate }, kindOf);
        return admitted.Count == 0 ? samples.ToList() : WithMode(samples, timeline);
    }
}
