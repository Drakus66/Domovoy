// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Ml;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pure unit tests for the ML.NET schedule trainer (roadmap Epic 2A). No infrastructure — fast.
/// </summary>
public sealed class MlTrainerTests
{
    [Fact]
    public void Train_FitsSchedule_And_RoundTrips()
    {
        // Synthetic 14 days of hourly data: value depends on hour-of-day (a daily schedule).
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new List<(DateTime, double)>();
        for (var h = 0; h < 24 * 14; h++)
        {
            var ts = start.AddHours(h);
            var value = 18.0 + 4.0 * Math.Sin(ts.Hour / 24.0 * 2 * Math.PI); // 14..22 over the day
            samples.Add((ts, value));
        }

        var trainer = new MlTrainer();
        var result = trainer.Train(samples, minSamples: 20);

        Assert.NotNull(result);
        Assert.Equal(samples.Count, result!.SampleCount);
        Assert.True(result.Artifact.Length > 0, "model artifact should be non-empty");
        Assert.True(result.Rmse >= 0 && !double.IsNaN(result.Rmse), "RMSE should be a finite non-negative number");

        // Backtest scorecard (Epic 2B): a chronological holdout MAE is computed on unseen samples.
        Assert.True(result.HoldoutCount > 0, "should hold out a recent slice for the backtest");
        Assert.NotNull(result.HoldoutMae);
        var mae = result.HoldoutMae!.Value;
        Assert.True(mae >= 0 && !double.IsNaN(mae), "holdout MAE should be finite and non-negative");
    }

    [Fact]
    public void Train_ReportsHoldoutAsNotEvaluated_WhenTheWindowIsTooThinToSplit()
    {
        // 22 samples with minSamples 20: a 20% holdout (4 rows) leaves 18 to train on — below minSamples, so the
        // holdout cannot be measured honestly. The model still trains (it's all the house has), but the score
        // must come back as "not evaluated" rather than 0: a zero MAE reads as a flawless model, wins template
        // selection against a genuinely measured candidate and clears the zone-promotion margin on pure noise.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var thin = Enumerable.Range(0, 22).Select(i => (start.AddHours(i), 20.0 + i % 3)).ToList();

        var result = new MlTrainer().Train(thin, minSamples: 20);

        Assert.NotNull(result);
        Assert.Null(result!.HoldoutMae);
        Assert.Equal(0, result.HoldoutCount);
    }

    [Fact]
    public void Train_ReturnsNull_BelowMinSamples()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var few = Enumerable.Range(0, 5).Select(i => (start.AddHours(i), 20.0)).ToList();
        Assert.Null(new MlTrainer().Train(few, minSamples: 20));
    }
}
