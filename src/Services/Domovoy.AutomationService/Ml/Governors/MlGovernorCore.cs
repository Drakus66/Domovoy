// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Value-type-agnostic bits shared by every ML consumer — the scalar/toggle <see cref="MlGovernorBase"/>, the
/// categorical <see cref="MlSelectorGovernor"/> and the <c>ml_predictor</c> source block (roadmap Epic 2Q,
/// Phase 3, consolidating the duplication flagged in Epic 2I). Holds the model-scope fallback chain, the
/// version pin, and the rolling drift window so those are defined once regardless of whether the proposal is a
/// number or a class label.
/// </summary>
public static class MlGovernorCore
{
    /// <summary>
    /// The instance's model-scope fallback chain, most specific first (Epic 2I): zone → zone_kind → global.
    /// The predictor returns the first scope with a loaded model, so a bedroom uses its own model if trained,
    /// else the shared zone-kind model, else the house-wide one.
    /// </summary>
    public static IReadOnlyList<ModelScope> BuildScopeChain(IBlockContext ctx)
    {
        var chain = new List<ModelScope>(3);
        if (!string.IsNullOrEmpty(ctx.ZoneId)) chain.Add(ModelScope.Zone(ctx.ZoneId));
        if (!string.IsNullOrEmpty(ctx.ZoneKind)) chain.Add(ModelScope.ZoneKind(ctx.ZoneKind));
        chain.Add(ModelScope.Global);
        return chain;
    }

    /// <summary>The instance's pinned model version (Epic 2C model_selection), or 0 for latest.</summary>
    public static int PinnedVersion(IBlockContext ctx) =>
        (int)Math.Max(0, Math.Round(ctx.Param(MlGovernorBase.ModelVersionParam, 0)));
}

/// <summary>
/// A rolling window of per-tick disagreements feeding the drift monitor (roadmap Epic 2Q, Phase 3). Shared by
/// the governors so "push an error, evict the old, take the window mean" isn't re-implemented per value-type.
/// </summary>
public sealed class DriftWindow
{
    private readonly Queue<(DateTimeOffset At, double Error)> _errors = new();

    /// <summary>Push a disagreement and return the mean over the last <paramref name="windowMin"/> minutes (null until seeded).</summary>
    public double? Push(DateTimeOffset now, double error, double windowMin)
    {
        _errors.Enqueue((now, error));
        while (_errors.Count > 0 && (now - _errors.Peek().At).TotalMinutes > windowMin)
            _errors.Dequeue();
        return _errors.Count == 0 ? null : _errors.Average(e => e.Error);
    }
}
