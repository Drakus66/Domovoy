using Domovoy.AutomationService.Services.Discovery;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the discrete Granger-causality screen (roadmap Epic 2F). Verifies it fires when the sensor's
/// previous slot genuinely predicts the action, and stays silent when the action is explained by its own lag.
/// Pure — deterministic synthetic slot series, no infrastructure.
/// </summary>
public sealed class GrangerCausalityTests
{
    [Fact]
    public void Detects_LaggedCausation()
    {
        // Sensor cycles 0,1,2; the action fires in the slot AFTER the sensor hits its high state (2).
        var n = 60;
        var x = new int[n];
        var y = new int[n];
        var present = new bool[n];
        for (var t = 0; t < n; t++)
        {
            x[t] = t % 3;
            present[t] = true;
            y[t] = t > 0 && x[t - 1] == 2 ? 1 : 0;
        }

        var p = GrangerCausality.PValue(x, y, present);
        Assert.True(p < 0.01, $"expected strong causation, got p={p}");
    }

    [Fact]
    public void Silent_WhenActionExplainedByItsOwnLag()
    {
        // The action simply alternates (fully determined by its own previous value); the sensor is unrelated.
        var n = 60;
        var x = new int[n];
        var y = new int[n];
        var present = new bool[n];
        for (var t = 0; t < n; t++)
        {
            x[t] = t % 3;      // unrelated sensor cycle
            y[t] = t % 2;      // action determined entirely by y_{t-1}
            present[t] = true;
        }

        var p = GrangerCausality.PValue(x, y, present);
        Assert.True(p > 0.5, $"expected no added predictive power, got p={p}");
    }

    [Fact]
    public void ReturnsOne_OnTooLittleData()
    {
        var p = GrangerCausality.PValue(new[] { 0, 1 }, new[] { 0, 1 }, new[] { true, true });
        Assert.Equal(1.0, p);
    }
}
