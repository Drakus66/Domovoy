// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services.Discovery;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Weekly digest (Epic 3J tail 5, extends 2N): composes 3–5 diary-tone lines on the week's intervention
/// dynamics, findings and amendments — and stays silent ("silence is a feature") when there is nothing to say.
/// </summary>
public sealed class WeeklyDigestBuilderTests
{
    [Fact]
    public void QuietWeek_ReturnsNull()
    {
        Assert.Null(WeeklyDigestBuilder.Build(new WeeklyDigestBuilder.Metrics(0, 0, 0, 0)));
    }

    [Fact]
    public void NoOverrides_ReadsAsInTheRhythm()
    {
        var text = WeeklyDigestBuilder.Build(new WeeklyDigestBuilder.Metrics(Firings: 12, Overrides: 0, NewFindings: 0, Amendments: 0));
        Assert.NotNull(text);
        Assert.Contains("ритм", text);
    }

    [Fact]
    public void ReportsInterventionsFindingsAndAmendments()
    {
        var text = WeeklyDigestBuilder.Build(new WeeklyDigestBuilder.Metrics(Firings: 20, Overrides: 4, NewFindings: 2, Amendments: 1));
        Assert.NotNull(text);
        Assert.Contains("вмешались", text);
        Assert.Contains("наблюдений", text);
        Assert.Contains("правок", text);
    }

    [Fact]
    public void FindingsOnly_StillSpeaks()
    {
        var text = WeeklyDigestBuilder.Build(new WeeklyDigestBuilder.Metrics(Firings: 0, Overrides: 0, NewFindings: 3, Amendments: 0));
        Assert.NotNull(text);
        Assert.Contains("наблюдений", text);
    }
}
