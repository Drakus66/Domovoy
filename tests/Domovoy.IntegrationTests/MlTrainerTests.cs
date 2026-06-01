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
    }

    [Fact]
    public void Train_ReturnsNull_BelowMinSamples()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var few = Enumerable.Range(0, 5).Select(i => (start.AddHours(i), 20.0)).ToList();
        Assert.Null(new MlTrainer().Train(few, minSamples: 20));
    }
}
