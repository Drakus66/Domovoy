// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Ml.Templates;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pure unit tests for the boolean → binary-classification template (roadmap Epic 2I, Phase 2): a learned
/// on/off schedule. No infrastructure — fast.
/// </summary>
public sealed class ScheduleBinaryTemplateTests
{
    [Fact]
    public void Trains_OnOffSchedule_And_RoundTrips()
    {
        // 14 days hourly: "on" during the day (08:00–22:00), "off" at night — a learnable daily schedule.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new List<LabeledSample>();
        for (var h = 0; h < 24 * 14; h++)
        {
            var ts = start.AddHours(h);
            var on = ts.Hour is >= 8 and < 22;
            samples.Add(new LabeledSample(ts, on ? 1 : 0));
        }

        var result = new ScheduleBinaryTemplate().Train(samples, minSamples: 20);

        Assert.NotNull(result);
        Assert.Equal(samples.Count, result!.SampleCount);
        Assert.True(result.Artifact.Length > 0);
        Assert.True(result.HoldoutCount > 0, "should hold out a recent slice");
        // Linear logistic on raw hour-of-day can't perfectly separate a non-monotonic day/night step
        // (richer time features are Phase 4), but it must learn clear signal above chance.
        Assert.True(result.HoldoutScore > 0.7, $"AUC should beat chance, was {result.HoldoutScore:0.###}");
    }

    [Fact]
    public void ReturnsNull_WhenSingleClass()
    {
        // Always-off history carries no decision boundary → no model.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var allOff = Enumerable.Range(0, 100).Select(i => new LabeledSample(start.AddHours(i), 0)).ToList();
        Assert.Null(new ScheduleBinaryTemplate().Train(allOff, minSamples: 20));
    }

    [Fact]
    public void Declares_BooleanTarget_AndHigherIsBetter()
    {
        var t = new ScheduleBinaryTemplate();
        Assert.Equal(CapabilityKind.Boolean, t.Target);
        Assert.Equal(MlModelKinds.ScheduleBinary, t.Kind);
        Assert.False(t.LowerIsBetter);
    }
}
