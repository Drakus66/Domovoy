// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Configuration;
using Domovoy.AutomationService.Services.Discovery;
using Domovoy.Contracts.Automations;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Self-correcting rules (Epic 3J tail 2): a rule the household overrides only in one time band should be
/// <b>amended</b> (add a time-of-day exception), not retired. A rule fought everywhere is the dead-rule path's
/// job, not this one. Pure/offline over the per-firing signal.
/// </summary>
public sealed class OverrideMinerTests
{
    private static InterventionMiner.Firing F(int hour, bool overridden)
    {
        var at = new DateTime(2026, 7, 1, hour, 0, 0, DateTimeKind.Utc);
        return new InterventionMiner.Firing("ruleA", "light1", "on_off", at, overridden, overridden ? at.AddSeconds(30) : null);
    }

    [Fact]
    public void ProposesTimeBandException_WhenFoughtOnlyInTheEvening()
    {
        var firings = new List<InterventionMiner.Firing>();
        // Daytime (09:00–13:00): fires and is left alone.
        for (var h = 9; h <= 13; h++) { firings.Add(F(h, false)); firings.Add(F(h, false)); } // 10 firings, 0 overrides
        // Evening (18:00–20:00): fought most of the time.
        for (var h = 18; h <= 20; h++) { firings.Add(F(h, true)); firings.Add(F(h, true)); }   // 6 firings, 6 overrides

        var candidates = OverrideMiner.Mine(firings, new AutomationOptions());

        var c = Assert.Single(candidates);
        Assert.Equal("ruleA", c.RuleId);
        Assert.Equal(18, c.FromHour);
        Assert.Equal(21, c.ToHour); // exclusive upper bound (max fought hour 20 + 1)

        // The exception restricts the rule to run only OUTSIDE 18:00–21:00 (complement window, wraps midnight).
        var cond = OverrideMiner.ExceptionCondition(c);
        Assert.Equal(ConditionType.TimeOfDay, cond.Type);
        Assert.Equal("21:00", cond.FromTime);
        Assert.Equal("18:00", cond.ToTime);
    }

    [Fact]
    public void DoesNotRefine_WhenFoughtEverywhere()
    {
        // Overridden across the whole day → overall rate ≥ dead-rule threshold → the retire path, not refine.
        var firings = new List<InterventionMiner.Firing>();
        for (var h = 9; h <= 20; h++) firings.Add(F(h, true));

        Assert.Empty(OverrideMiner.Mine(firings, new AutomationOptions()));
    }

    [Fact]
    public void DoesNotRefine_WhenNoBandCrossesTheThreshold()
    {
        // 20 firings, a few scattered overrides, no single hour reaching the 60% context rate.
        var firings = new List<InterventionMiner.Firing>();
        for (var h = 9; h <= 18; h++) { firings.Add(F(h, false)); firings.Add(F(h, h == 12)); } // one override total

        Assert.Empty(OverrideMiner.Mine(firings, new AutomationOptions()));
    }
}
