using Domovoy.AutomationService.Ml.Templates;

using Microsoft.ML;

using Xunit;

using CtxSample = Domovoy.AutomationService.Ml.Templates.ContextScheduleRegressionTemplate.ContextSample;
using CtxPrediction = Domovoy.AutomationService.Ml.Templates.ContextScheduleRegressionTemplate.ContextPrediction;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the Epic 2B context-join + context-aware regression: the as-of home-mode join and a model
/// that learns a mode-dependent setpoint (Night vs Home). Pure — no infrastructure.
/// </summary>
public sealed class ContextRegressionTests
{
    // --- Context-join (as-of) ---------------------------------------------------------------------

    [Fact]
    public void ModeAt_ReturnsLastChangeAtOrBefore()
    {
        var timeline = new List<(DateTime, string)>
        {
            (new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), "Home"),
            (new DateTime(2026, 1, 1, 22, 0, 0, DateTimeKind.Utc), "Night"),
        };

        Assert.Null(ContextFeatureJoin.ModeAt(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc), timeline)); // before any
        Assert.Equal("Home", ContextFeatureJoin.ModeAt(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), timeline));
        Assert.Equal("Night", ContextFeatureJoin.ModeAt(new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc), timeline));
    }

    [Fact]
    public void WithMode_AttachesModeToSamples()
    {
        var samples = new List<LabeledSample>
        {
            new(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), 21),
            new(new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc), 19),
        };
        var timeline = new List<(DateTime, string)>
        {
            (new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), "Home"),
            (new DateTime(2026, 1, 1, 22, 0, 0, DateTimeKind.Utc), "Night"),
        };

        var joined = ContextFeatureJoin.WithMode(samples, timeline);
        Assert.Equal("Home", joined[0].Mode);
        Assert.Equal("Night", joined[1].Mode);
    }

    // --- Context regression template --------------------------------------------------------------

    [Fact]
    public void Declines_OnSingleMode()
    {
        // One mode carries no context signal → the template declines so the plain schedule model wins selection.
        var samples = Enumerable.Range(0, 40)
            .Select(d => new LabeledSample(new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc).AddDays(d), 21, Mode: "Home"))
            .ToList();

        Assert.Null(new ContextScheduleRegressionTemplate().Train(samples, minSamples: 20));
    }

    [Fact]
    public void Learns_ModeDependentSetpoint()
    {
        // Same hour, but Night → 19° and Home → 22°. The model must separate them by mode alone.
        var start = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var samples = new List<LabeledSample>();
        for (var d = 0; d < 60; d++)
        {
            var isNight = d % 2 == 0;
            samples.Add(new LabeledSample(start.AddDays(d), isNight ? 19 : 22, Mode: isNight ? "Night" : "Home"));
        }

        var result = new ContextScheduleRegressionTemplate().Train(samples, minSamples: 20);
        Assert.NotNull(result);

        var ml = new MLContext(seed: 0);
        using var ms = new MemoryStream(result!.Artifact);
        var model = ml.Model.Load(ms, out _);
        var engine = ml.Model.CreatePredictionEngine<CtxSample, CtxPrediction>(model);

        var night = engine.Predict(new CtxSample { Hour = 20, Dow = 1, Mode = "Night" }).Value;
        var home = engine.Predict(new CtxSample { Hour = 20, Dow = 1, Mode = "Home" }).Value;

        Assert.True(night < home - 1.5, $"expected mode separation, got Night={night} Home={home}");
    }
}
