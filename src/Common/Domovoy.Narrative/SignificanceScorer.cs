// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.Contracts.Narrative;

namespace Domovoy.Narrative;

/// <summary>Baseline context for significance scoring (roadmap Epic 2N, Phase 2) — supplied by the builder.</summary>
public sealed class SignificanceContext
{
    /// <summary>Occurrences of each <c>archetype.capability.Transition</c> over the baseline window — drives rarity.</summary>
    public IReadOnlyDictionary<string, int> PatternCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Site time zone for local-hour anomaly detection; null → UTC.</summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>Devices currently considered offline (liveness watchdog) — an offline transition is notable.</summary>
    public IReadOnlySet<string>? OfflineDeviceIds { get; init; }
}

/// <summary>
/// Scores a <see cref="Scene"/>'s narrative significance (roadmap Epic 2N, Phase 2) — the diary's main
/// filter. A transparent, explainable weighted sum: rarity (first «похолодание» of the season), mode
/// (Vacation is always notable), human presence (people &gt; automation), odd-hour and offline anomalies,
/// causal richness and coalesced breadth. Every contributor also records a reason for audit and future
/// LLM hints. Pure and deterministic.
/// </summary>
public static class SignificanceScorer
{
    public const double Base = 1.0;
    public const double HumanWeight = 2.0;
    public const double VacationWeight = 3.0;
    public const double OddHourWeight = 2.0;
    public const double OfflineWeight = 2.5;
    public const double RarityMax = 2.5;
    public const double CausalWeight = 0.5;
    public const double BreadthPerBeat = 0.5;
    public const double BreadthCap = 1.5;

    /// <summary>Score the scene and write <see cref="Scene.Significance"/> + <see cref="Scene.SignificanceReasons"/>.</summary>
    public static void ScoreInto(Scene scene, SignificanceContext ctx)
    {
        var (score, reasons) = Score(scene, ctx);
        scene.Significance = score;
        scene.SignificanceReasons = reasons;
    }

    public static (double Score, List<string> Reasons) Score(Scene scene, SignificanceContext ctx)
    {
        var reasons = new List<string>();
        var score = Base;

        if (scene.Actor == PersonaRole.Residents)
        {
            score += HumanWeight;
            reasons.Add("human");
        }

        if (string.Equals(scene.Mode, WellKnownModes.Vacation, StringComparison.OrdinalIgnoreCase))
        {
            score += VacationWeight;
            reasons.Add("mode:Vacation");
        }

        // Odd-hour anomaly (local 00:00–05:59).
        var localHour = ToLocalHour(scene.StartedAt, ctx.TimeZone);
        if (localHour is >= 0 and <= 5)
        {
            score += OddHourWeight;
            reasons.Add("anomaly:odd-hour");
        }

        // Offline anomaly — a device that went unreachable.
        if (ctx.OfflineDeviceIds is { Count: > 0 } &&
            scene.Beats.Any(b => b.DeviceId is not null && ctx.OfflineDeviceIds.Contains(b.DeviceId)))
        {
            score += OfflineWeight;
            reasons.Add("anomaly:offline");
        }

        // Rarity — rarer patterns score higher; common ones add nothing.
        var count = MinPatternCount(scene, ctx.PatternCounts);
        if (count >= 0)
        {
            var rarity = Math.Max(0, RarityMax - count * 0.5);
            if (rarity > 0)
            {
                score += rarity;
                if (count <= 2) reasons.Add("rarity");
            }
        }

        if (scene.CauseBeat is not null)
        {
            score += CausalWeight;
            reasons.Add("causal");
        }

        if (scene.Beats.Count > 1)
        {
            var breadth = Math.Min(BreadthCap, BreadthPerBeat * (scene.Beats.Count - 1));
            score += breadth;
            reasons.Add("breadth");
        }

        return (score, reasons);
    }

    private static int MinPatternCount(Scene scene, IReadOnlyDictionary<string, int> counts)
    {
        var min = int.MaxValue;
        foreach (var b in scene.Beats)
        {
            var key = $"{b.ArchetypeKey}.{b.CapabilityId}.{b.Transition}";
            min = Math.Min(min, counts.TryGetValue(key, out var c) ? c : 0);
        }
        return scene.Beats.Count == 0 ? -1 : min;
    }

    private static int ToLocalHour(DateTime utc, TimeZoneInfo? tz)
    {
        var instant = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = tz is null ? instant : TimeZoneInfo.ConvertTimeFromUtc(instant, tz);
        return local.Hour;
    }
}
