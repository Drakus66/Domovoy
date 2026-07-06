namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Chi-squared survival function via the regularized incomplete gamma function — turns a mutual-information
/// score into a p-value (roadmap Epic 2F, Stage 1). Under the null hypothesis of independence, the G-statistic
/// <c>G = 2·N·MI</c> (MI in nats) is asymptotically χ² with <c>(|X|−1)(|Y|−1)</c> degrees of freedom, so
/// <c>p = P(χ²_df &gt; G)</c> is the chance of seeing a dependency this strong by luck. Those p-values feed the
/// Benjamini-Hochberg FDR control in <see cref="Statistics"/> — without it, screening thousands of sensor×device
/// pairs would surface a flood of false "significant" ones. Deterministic (no RNG), so tests are exact.
/// </summary>
public static class ChiSquared
{
    /// <summary>G-statistic for a mutual-information score: <c>G = 2·N·MI</c> (MI in nats, N = sample count).</summary>
    public static double GStatistic(double miNats, int n) => 2.0 * n * miNats;

    /// <summary>p-value P(χ²_df &gt; x). Returns 1 for x≤0 or df≤0 (no evidence of dependence).</summary>
    public static double SurvivalFunction(double x, int df)
    {
        if (df <= 0 || x <= 0) return 1.0;
        return RegularizedGammaQ(df / 2.0, x / 2.0);
    }

    // --- Numerical Recipes style regularized incomplete gamma: P(a,x) via series, Q(a,x) via continued fraction. ---

    private const int MaxIterations = 200;
    private const double Epsilon = 1e-14;
    private const double FpMin = 1e-300;

    /// <summary>Regularized upper incomplete gamma Q(a,x) = 1 − P(a,x).</summary>
    public static double RegularizedGammaQ(double a, double x)
    {
        if (x < 0 || a <= 0) return double.NaN;
        if (x == 0) return 1.0;
        return x < a + 1.0 ? 1.0 - GammaSeries(a, x) : GammaContinuedFraction(a, x);
    }

    // Series expansion P(a,x), good for x < a+1.
    private static double GammaSeries(double a, double x)
    {
        var ap = a;
        var sum = 1.0 / a;
        var del = sum;
        for (var i = 0; i < MaxIterations; i++)
        {
            ap += 1.0;
            del *= x / ap;
            sum += del;
            if (Math.Abs(del) < Math.Abs(sum) * Epsilon) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - LnGamma(a));
    }

    // Lentz's continued fraction for Q(a,x), good for x >= a+1.
    private static double GammaContinuedFraction(double a, double x)
    {
        var b = x + 1.0 - a;
        var c = 1.0 / FpMin;
        var d = 1.0 / b;
        var h = d;
        for (var i = 1; i <= MaxIterations; i++)
        {
            var an = -i * (i - a);
            b += 2.0;
            d = an * d + b;
            if (Math.Abs(d) < FpMin) d = FpMin;
            c = b + an / c;
            if (Math.Abs(c) < FpMin) c = FpMin;
            d = 1.0 / d;
            var del = d * c;
            h *= del;
            if (Math.Abs(del - 1.0) < Epsilon) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LnGamma(a)) * h;
    }

    // Lanczos approximation of ln Γ(z).
    private static readonly double[] LanczosCoefficients =
    {
        676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012,
        9.9843695780195716e-6, 1.5056327351493116e-7,
    };

    private static double LnGamma(double z)
    {
        if (z < 0.5)
            return Math.Log(Math.PI / Math.Sin(Math.PI * z)) - LnGamma(1.0 - z);

        z -= 1.0;
        var x = 0.99999999999980993;
        for (var i = 0; i < LanczosCoefficients.Length; i++)
            x += LanczosCoefficients[i] / (z + i + 1);

        var t = z + LanczosCoefficients.Length - 0.5;
        return 0.5 * Math.Log(2 * Math.PI) + (z + 0.5) * Math.Log(t) - t + Math.Log(x);
    }
}
