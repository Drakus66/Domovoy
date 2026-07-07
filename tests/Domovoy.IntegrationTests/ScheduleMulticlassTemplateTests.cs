// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Ml.Templates;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pure unit tests for the enum → multiclass-classification template (roadmap Epic 2I, Phase 3): a learned
/// mode/level schedule. No infrastructure — fast.
/// </summary>
public sealed class ScheduleMulticlassTemplateTests
{
    [Fact]
    public void Trains_ModeSchedule_And_RoundTrips()
    {
        // 14 days hourly HVAC mode by time: night → "eco", morning/evening → "comfort", midday → "away".
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new List<LabeledSample>();
        for (var h = 0; h < 24 * 14; h++)
        {
            var ts = start.AddHours(h);
            var mode = ts.Hour switch
            {
                >= 0 and < 6 => "eco",
                >= 10 and < 16 => "away",
                _ => "comfort",
            };
            samples.Add(new LabeledSample(ts, 0, mode));
        }

        var result = new ScheduleMulticlassTemplate().Train(samples, minSamples: 20);

        Assert.NotNull(result);
        Assert.Equal(samples.Count, result!.SampleCount);
        Assert.True(result.Artifact.Length > 0);
        Assert.True(result.HoldoutCount > 0, "should hold out a recent slice");
        // A clean time-of-day schedule should be well above uniform (3 classes → 0.33 chance).
        Assert.True(result.HoldoutScore > 0.6, $"macro accuracy should beat chance, was {result.HoldoutScore:0.###}");
    }

    [Fact]
    public void ReturnsNull_WhenSingleClass()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oneMode = Enumerable.Range(0, 100).Select(i => new LabeledSample(start.AddHours(i), 0, "eco")).ToList();
        Assert.Null(new ScheduleMulticlassTemplate().Train(oneMode, minSamples: 20));
    }

    [Fact]
    public void Declares_EnumTarget()
    {
        var t = new ScheduleMulticlassTemplate();
        Assert.Equal(CapabilityKind.Enum, t.Target);
        Assert.Equal(MlModelKinds.ScheduleMulticlass, t.Kind);
        Assert.False(t.LowerIsBetter);
    }
}
