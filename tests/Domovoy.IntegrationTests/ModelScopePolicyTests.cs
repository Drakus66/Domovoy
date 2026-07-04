using Domovoy.AutomationService.Ml.Templates;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the per-zone auto-promotion gate (roadmap Epic 2I): a zone splinters into its own model
/// only when its own data beats the shared fallback by the margin, in the template's metric orientation.
/// </summary>
public sealed class ModelScopePolicyTests
{
    // Regression template: lower MAE is better.
    private static readonly ScheduleRegressionTemplate Regression = new();

    [Fact]
    public void Promotes_WhenCandidateBeatsFallbackByMargin()
    {
        // Candidate MAE 1.0 vs fallback 1.5, margin 0.25 → 1.0 <= 1.5 - 0.25 → promote.
        Assert.True(ModelTemplateRegistry.ShouldPromote(Regression, candidate: 1.0, fallback: 1.5, margin: 0.25));
    }

    [Fact]
    public void StaysOnFallback_WhenImprovementWithinMargin()
    {
        // Candidate only marginally better (1.4 vs 1.5, margin 0.25) → keep the shared model.
        Assert.False(ModelTemplateRegistry.ShouldPromote(Regression, candidate: 1.4, fallback: 1.5, margin: 0.25));
    }

    [Fact]
    public void StaysOnFallback_WhenCandidateWorse()
    {
        Assert.False(ModelTemplateRegistry.ShouldPromote(Regression, candidate: 2.0, fallback: 1.5, margin: 0.25));
    }
}
