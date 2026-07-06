using Domovoy.AutomationService.Services.Discovery;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the pattern-discovery statistics primitives (roadmap Epic 2F, Stage 1): mutual
/// information, the chi-squared p-value that turns an MI score into significance, and Benjamini-Hochberg FDR
/// control. Pure and deterministic — no infrastructure, no RNG.
/// </summary>
public sealed class DiscoveryStatisticsTests
{
    // ---- Mutual information ----

    [Fact]
    public void MI_OfPerfectlyDependent_IsLn2()
    {
        var x = Enumerable.Range(0, 100).Select(i => i % 2).ToList();
        var mi = InformationTheory.MutualInformation(x, x); // y == x
        Assert.Equal(Math.Log(2), mi, precision: 6);
    }

    [Fact]
    public void MI_OfIndependent_IsNearZero()
    {
        var x = Enumerable.Range(0, 100).Select(i => i % 2).ToList();
        var y = Enumerable.Range(0, 100).Select(i => (i / 2) % 2).ToList(); // varies on a different cycle
        Assert.True(InformationTheory.MutualInformation(x, y) < 1e-9);
    }

    [Fact]
    public void ConditionalMI_RemovesConfounderDependence()
    {
        // x and y both equal the confounder z, so plain MI(x;y) is high (ln2) but, held within a z-stratum,
        // x and y are constant — there is no residual dependence. This is the confounder case the screen must kill.
        var z = Enumerable.Range(0, 100).Select(i => i % 2).ToList();
        Assert.Equal(Math.Log(2), InformationTheory.MutualInformation(z, z), precision: 6);
        Assert.True(InformationTheory.ConditionalMutualInformation(z, z, z) < 1e-9);
    }

    // ---- Chi-squared survival (p-value) ----

    [Theory]
    [InlineData(3.8415, 1, 0.05)]   // χ²(1) 95th percentile
    [InlineData(5.9915, 2, 0.05)]   // χ²(2) 95th percentile
    [InlineData(11.345, 3, 0.01)]   // χ²(3) 99th percentile
    public void ChiSquared_SurvivalMatchesKnownCriticalValues(double x, int df, double expectedP)
    {
        Assert.Equal(expectedP, ChiSquared.SurvivalFunction(x, df), precision: 3);
    }

    [Fact]
    public void ChiSquared_ZeroOrNegative_IsOne()
    {
        Assert.Equal(1.0, ChiSquared.SurvivalFunction(0, 3));
        Assert.Equal(1.0, ChiSquared.SurvivalFunction(-5, 3));
    }

    [Fact]
    public void ChiSquared_StrongStatistic_IsTiny()
    {
        Assert.True(ChiSquared.SurvivalFunction(100, 1) < 1e-15);
    }

    // ---- Benjamini-Hochberg FDR ----

    [Fact]
    public void BH_AcceptsAll_WhenEveryPValueOnTheLine()
    {
        var p = new[] { 0.01, 0.02, 0.03, 0.04, 0.05 };
        var accepted = Statistics.BenjaminiHochberg(p, 0.05);
        Assert.All(accepted, Assert.True);
    }

    [Fact]
    public void BH_AcceptsOnlySmallest_WhenRestAreLarge()
    {
        var p = new[] { 0.001, 0.7, 0.8, 0.9, 0.95 };
        var accepted = Statistics.BenjaminiHochberg(p, 0.05);
        Assert.True(accepted[0]);
        Assert.All(accepted.Skip(1), Assert.False);
    }

    [Fact]
    public void BH_RejectsAll_WhenNoneSignificant()
    {
        var p = new[] { 0.2, 0.4, 0.6, 0.8 };
        Assert.All(Statistics.BenjaminiHochberg(p, 0.05), Assert.False);
    }
}
