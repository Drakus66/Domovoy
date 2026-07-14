// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.Contracts.Ml;

namespace Domovoy.AutomationService.Ml.Governors;

/// <summary>
/// Enum → selector governor (roadmap Epic 2I, Phase 3) — the categorical sibling of the setpoint/toggle
/// governors. The model proposes which enum value to select (a learned mode/level schedule); the governor
/// commands it on a deterministic loop under the same Shadow → Bounded → Full authority staging. Bounded-Active
/// limits each change to within <c>maxClassStep</c> positions of the current value along the declared
/// <c>Values</c> order (no jumping straight from "off" to "boost"); Full applies the proposal directly. The
/// drift signal is the rolling rate at which the predicted class disagrees with the measured one, so a stale
/// schedule auto-demotes to Shadow.
///
/// <para>It is self-contained (not built on <see cref="MlGovernorBase"/>, which is scalar-shaped) because its
/// proposal is a class label, not a number — but it mirrors the same staging/drift contract and emits the same
/// observational outputs.</para>
/// </summary>
public sealed class MlSelectorGovernor : IBlock
{
    private const int Shadow = 0;
    private const int Bounded = 1;
    private const int Full = 2;

    private readonly Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, string?> _predict;
    private readonly string _measuredInput;
    private readonly string _boundOutput;
    private readonly IReadOnlyList<string> _values;

    private readonly DriftWindow _drift = new();

    public MlSelectorGovernor(
        Func<DateTimeOffset, IReadOnlyList<ModelScope>, int, string?> predict,
        string measuredInput, string boundOutput, IReadOnlyList<string> values)
    {
        _predict = predict;
        _measuredInput = measuredInput;
        _boundOutput = boundOutput;
        _values = values;
    }

    public void Tick(IBlockContext ctx)
    {
        var configuredStage = (int)Math.Round(Math.Clamp(ctx.Param("stage", Shadow), Shadow, Full));

        var proposed = _predict(ctx.Now, MlGovernorCore.BuildScopeChain(ctx), MlGovernorCore.PinnedVersion(ctx));
        if (string.IsNullOrEmpty(proposed))
        {
            ctx.Emit(MlGovernorBase.EffectiveStage, (double)Shadow);
            return;
        }

        ctx.Emit(MlGovernorBase.ProposedSetpoint, proposed);

        var measured = AsClass(ctx.Read(_measuredInput));
        var drift = UpdateDrift(ctx, proposed, measured);
        if (drift is not null) ctx.Emit(MlGovernorBase.Drift, Math.Round(drift.Value, 4));

        var driftThreshold = Math.Max(0.0001, ctx.Param("driftThreshold", 0.5));
        var effectiveStage = drift is { } d && d > driftThreshold ? Shadow : configuredStage;
        ctx.Emit(MlGovernorBase.EffectiveStage, (double)effectiveStage);

        if (effectiveStage == Shadow)
        {
            if (configuredStage != Shadow && drift is { } dd && dd > driftThreshold)
                ctx.Log($"ML class drift {dd:0.###} > {driftThreshold:0.###} — auto-demoted to Shadow");
            return; // Shadow: no bound emit → runtime publishes no command.
        }

        var current = AsClass(ctx.Commanded(_boundOutput)) ?? measured;
        var target = effectiveStage == Bounded ? ClampToAdjacent(proposed, current, ctx) : proposed;
        ctx.Emit(_boundOutput, target); // bound → commands the deterministic loop (1D actuation)
    }

    // Bounded-Active: don't move more than maxClassStep positions from the current value along the Values order.
    private string ClampToAdjacent(string proposed, string? current, IBlockContext ctx)
    {
        if (current is null) return proposed;
        var from = IndexOf(current);
        var to = IndexOf(proposed);
        if (from < 0 || to < 0) return proposed; // unknown value → don't second-guess

        var maxStep = Math.Max(1, (int)Math.Round(ctx.Param("maxClassStep", 1)));
        var clamped = Math.Clamp(to, from - maxStep, from + maxStep);
        return _values[clamped];
    }

    private int IndexOf(string value)
    {
        for (var i = 0; i < _values.Count; i++)
            if (string.Equals(_values[i], value, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // Drift = predicted class vs measured class, so the rolling mean is the misclassification rate.
    private double? UpdateDrift(IBlockContext ctx, string proposed, string? measured)
    {
        if (measured is null) return null;

        var windowMin = Math.Max(1, ctx.Param("driftWindowMin", 60));
        var error = string.Equals(proposed, measured, StringComparison.OrdinalIgnoreCase) ? 0.0 : 1.0;
        return _drift.Push(ctx.Now, error, windowMin);
    }

    private static string? AsClass(object? v) => v switch
    {
        null => null,
        string s when !string.IsNullOrWhiteSpace(s) => s,
        string => null,
        _ => v.ToString(),
    };
}
