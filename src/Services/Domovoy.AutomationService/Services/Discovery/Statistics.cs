namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Multiple-comparison control for the screening stage (roadmap Epic 2F, Stage 1). When thousands of
/// sensor×device pairs are each tested for dependence, a raw p&lt;0.05 threshold alone would let ~5% of the
/// truly-independent pairs through as false positives — enough to swamp the real signal. Benjamini-Hochberg
/// caps the expected <b>false-discovery rate</b> (fraction of accepted pairs that are false) at <c>q</c>,
/// which is the right guarantee when the whole point is to promote only pairs worth the expensive downstream
/// mining/validation.
/// </summary>
public static class Statistics
{
    /// <summary>
    /// Benjamini-Hochberg step-up: returns a parallel mask of which p-values are accepted at false-discovery
    /// rate <paramref name="q"/>. Finds the largest rank k with <c>p(k) ≤ (k/m)·q</c> and accepts every pair
    /// with a p-value at or below that one (so ties are handled correctly). Empty input ⇒ empty mask.
    /// </summary>
    public static bool[] BenjaminiHochberg(IReadOnlyList<double> pValues, double q)
    {
        var m = pValues.Count;
        var accepted = new bool[m];
        if (m == 0) return accepted;

        // Ranks 1..m by ascending p-value.
        var order = Enumerable.Range(0, m).OrderBy(i => pValues[i]).ToArray();

        // Largest k where p(k) ≤ (k/m)·q; everything up to that rank is accepted.
        var cutoffRank = -1;
        for (var rank = 1; rank <= m; rank++)
        {
            if (pValues[order[rank - 1]] <= (double)rank / m * q)
                cutoffRank = rank;
        }
        if (cutoffRank < 0) return accepted; // nothing significant

        for (var rank = 1; rank <= cutoffRank; rank++)
            accepted[order[rank - 1]] = true;
        return accepted;
    }
}
