namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Discrete Granger-style causality screen (roadmap Epic 2F). Conditional mutual information (Stage 1) tells us
/// a sensor and an action are <i>dependent</i>; Granger adds the arrow of time: does the sensor's <b>previous</b>
/// state improve prediction of the action <b>now</b>, beyond the action's own recent history? That distinguishes
/// "the sensor leads the action" from "they merely co-vary". Implemented as a likelihood-ratio G-test between two
/// nested multinomial models estimated from slot counts (no optimizer, deterministic):
/// <list type="bullet">
///   <item><b>Restricted:</b> P(action_t | action_{t-1}) — the action from its own lag only.</item>
///   <item><b>Full:</b> P(action_t | action_{t-1}, sensor_{t-1}) — adds the sensor's lag.</item>
/// </list>
/// <c>G = 2·(LL_full − LL_restricted)</c> is asymptotically χ² with <c>(|sensor|−1)·|action_lag|·(|action|−1)</c>
/// degrees of freedom, so a small p-value means the sensor's past genuinely carries predictive information about
/// the action's future. Only slot pairs where both the current and previous slot have a known sensor value count.
/// </summary>
public static class GrangerCausality
{
    /// <summary>
    /// p-value that the discretized sensor series <paramref name="x"/> Granger-causes the binary action series
    /// <paramref name="y"/> (aligned by slot; <paramref name="present"/> marks slots with a known sensor value).
    /// Returns 1 (no evidence) when there is too little consecutive data or no variation.
    /// </summary>
    public static double PValue(IReadOnlyList<int> x, IReadOnlyList<int> y, IReadOnlyList<bool> present)
    {
        var n = Math.Min(x.Count, y.Count);

        // Counts: restricted[(yPrev, yt)], full[(xPrev, yPrev, yt)]. Also track the distinct sensor states seen.
        var restricted = new Dictionary<(int, int), int>();
        var full = new Dictionary<(int, int, int), int>();
        var sensorStates = new HashSet<int>();
        var samples = 0;

        for (var t = 1; t < n; t++)
        {
            if (!present[t] || !present[t - 1]) continue; // need a consecutive, known lag
            var xPrev = x[t - 1];
            var yPrev = y[t - 1];
            var yt = y[t];

            restricted.TryGetValue((yPrev, yt), out var rc); restricted[(yPrev, yt)] = rc + 1;
            full.TryGetValue((xPrev, yPrev, yt), out var fc); full[(xPrev, yPrev, yt)] = fc + 1;
            sensorStates.Add(xPrev);
            samples++;
        }

        if (samples < 6 || sensorStates.Count < 2) return 1.0; // nothing to say

        // Context totals for the conditional MLEs.
        var restrictedCtx = new Dictionary<int, int>();       // yPrev → total
        foreach (var ((yPrev, _), c) in restricted) restrictedCtx.TryAdd(yPrev, 0);
        foreach (var ((yPrev, _), c) in restricted) restrictedCtx[yPrev] += c;

        var fullCtx = new Dictionary<(int, int), int>();      // (xPrev, yPrev) → total
        foreach (var ((xPrev, yPrev, _), c) in full)
        {
            fullCtx.TryGetValue((xPrev, yPrev), out var tot); fullCtx[(xPrev, yPrev)] = tot + c;
        }

        // Log-likelihoods (MLE): every observed outcome has count ≥ 1, so the conditional probability is > 0.
        double llRestricted = 0;
        foreach (var ((yPrev, _), c) in restricted)
            llRestricted += c * Math.Log((double)c / restrictedCtx[yPrev]);

        double llFull = 0;
        foreach (var ((xPrev, yPrev, _), c) in full)
            llFull += c * Math.Log((double)c / fullCtx[(xPrev, yPrev)]);

        var g = 2.0 * (llFull - llRestricted);
        if (g <= 0) return 1.0;

        // df = (|sensor|−1) · |yPrev states| · (|yt states|−1). yt is binary here → (·)·(2−1).
        var yPrevStates = restrictedCtx.Count;
        var df = Math.Max(1, (sensorStates.Count - 1) * Math.Max(1, yPrevStates) * 1);
        return ChiSquared.SurvivalFunction(g, df);
    }
}
