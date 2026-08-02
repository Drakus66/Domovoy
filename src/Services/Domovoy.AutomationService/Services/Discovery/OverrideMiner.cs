// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Self-correcting rules (roadmap Epic 3J tail 2): instead of only retiring a rule the household fights
/// <i>everywhere</i> (the dead-rule path), find one the household fights only in a specific <b>context</b> and
/// propose <b>amending</b> it — add an exception so it stops misfiring there while keeping the behaviour that
/// works. v1 uses the time-of-day context (the "evenings" case): if a rule's overrides concentrate in an
/// hour-band, propose a time-of-day guard that runs the rule only <i>outside</i> that band.
///
/// <para>Pure over the per-firing signal from <see cref="InterventionMiner.Firings"/>. Only rules whose overall
/// override-rate is below the dead-rule threshold qualify — a rule fought everywhere is a retire case, not a
/// refine one, so the two miners never both fire on the same rule.</para>
/// </summary>
public static class OverrideMiner
{
    /// <summary>A rule fought in a single time band [<see cref="FromHour"/>, <see cref="ToHour"/>) — the exception to add.</summary>
    public sealed record RefineCandidate(string RuleId, int FromHour, int ToHour, int Firings, int Overrides, double OverrideRate);

    public static List<RefineCandidate> Mine(IReadOnlyList<InterventionMiner.Firing> firings, AutomationOptions options)
    {
        var result = new List<RefineCandidate>();

        foreach (var byRule in firings.GroupBy(f => f.RuleId, StringComparer.Ordinal))
        {
            var all = byRule.ToList();
            if (all.Count == 0) continue;

            var totalOverrides = all.Count(f => f.Overridden);
            var overallRate = (double)totalOverrides / all.Count;
            // A rule fought everywhere is the retire path's job — refine only rules that are otherwise fine.
            if (overallRate >= options.DeadRuleMinOverrideRate) continue;

            var byHour = new int[24];
            var ovByHour = new int[24];
            foreach (var f in all) { byHour[f.FiredAt.Hour]++; if (f.Overridden) ovByHour[f.FiredAt.Hour]++; }

            var foughtHours = Enumerable.Range(0, 24)
                .Where(h => byHour[h] > 0 && (double)ovByHour[h] / byHour[h] >= options.RefineMinContextOverrideRate)
                .ToList();
            if (foughtHours.Count == 0) continue;

            // v1: one contiguous band spanning the fought hours (the common single-band "evenings" case).
            int from = foughtHours.Min(), to = foughtHours.Max();
            int bandFirings = 0, bandOverrides = 0;
            for (var h = from; h <= to; h++) { bandFirings += byHour[h]; bandOverrides += ovByHour[h]; }

            if (bandFirings < options.RefineMinContextFirings) continue;
            var bandRate = (double)bandOverrides / bandFirings;
            if (bandRate < options.RefineMinContextOverrideRate) continue;
            // An exception that covers ~every firing would gut the rule — that's a dead rule, not a refine.
            if (bandFirings >= all.Count) continue;

            result.Add(new RefineCandidate(byRule.Key, from, to + 1, bandFirings, bandOverrides, Math.Round(bandRate, 3)));
        }

        return result.OrderByDescending(c => c.OverrideRate).ThenByDescending(c => c.Firings).ToList();
    }

    /// <summary>
    /// The time-of-day guard to append so the rule only runs <b>outside</b> the fought band. The allowed window is
    /// the complement of [FromHour, ToHour): from <see cref="RefineCandidate.ToHour"/> round to
    /// <see cref="RefineCandidate.FromHour"/> (wraps midnight — the condition supports From &gt; To).
    /// </summary>
    public static RuleCondition ExceptionCondition(RefineCandidate c) => new()
    {
        Type = ConditionType.TimeOfDay,
        FromTime = $"{c.ToHour % 24:D2}:00",
        ToTime = $"{c.FromHour:D2}:00",
    };
}
