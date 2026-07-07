// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// Pure information-theoretic measures over discrete (integer-labelled) samples — the cheap-screening
/// primitives of the pattern-discovery funnel (roadmap Epic 2F, Stage 1). Mutual information catches the
/// <b>non-linear</b> dependencies a correlation coefficient misses ("dark → light" is a threshold, not a line);
/// the <b>conditional</b> variant is what separates a real driver from a confounder — a light correlates with
/// low illuminance, time-of-day and presence at once, so we score I(sensor;action | time-of-day) to see whether
/// the sensor still explains the action once the obvious confounder is held fixed. Values are in <b>nats</b>
/// (natural log), which is what the G-test in <see cref="ChiSquared"/> expects.
/// </summary>
public static class InformationTheory
{
    /// <summary>
    /// Mutual information I(X;Y) in nats over paired label arrays. 0 ⇒ independent; higher ⇒ knowing X tells
    /// you more about Y. Returns 0 for empty/mismatched input rather than throwing (a pair with no data is
    /// simply uninformative).
    /// </summary>
    public static double MutualInformation(IReadOnlyList<int> x, IReadOnlyList<int> y)
    {
        var n = x.Count;
        if (n == 0 || y.Count != n) return 0;

        var joint = new Dictionary<(int, int), int>();
        var px = new Dictionary<int, int>();
        var py = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            joint[(x[i], y[i])] = joint.GetValueOrDefault((x[i], y[i])) + 1;
            px[x[i]] = px.GetValueOrDefault(x[i]) + 1;
            py[y[i]] = py.GetValueOrDefault(y[i]) + 1;
        }

        double mi = 0;
        foreach (var ((a, b), nab) in joint)
        {
            // p(a,b) * ln( p(a,b) / (p(a)p(b)) ), computed in counts to avoid repeated divisions.
            var pab = (double)nab / n;
            mi += pab * Math.Log((double)nab * n / ((double)px[a] * py[b]));
        }
        return mi < 0 ? 0 : mi; // clamp tiny negative round-off
    }

    /// <summary>
    /// Conditional mutual information I(X;Y|Z) in nats: the average of the within-stratum MI weighted by the
    /// probability of each Z stratum. A pattern that survives this — but would have looked strong under plain
    /// MI — is the confounder's doing (e.g. both the sensor and the light track time-of-day).
    /// </summary>
    public static double ConditionalMutualInformation(
        IReadOnlyList<int> x, IReadOnlyList<int> y, IReadOnlyList<int> z)
    {
        var n = x.Count;
        if (n == 0 || y.Count != n || z.Count != n) return 0;

        var strata = new Dictionary<int, (List<int> X, List<int> Y)>();
        for (var i = 0; i < n; i++)
        {
            if (!strata.TryGetValue(z[i], out var s)) { s = (new List<int>(), new List<int>()); strata[z[i]] = s; }
            s.X.Add(x[i]);
            s.Y.Add(y[i]);
        }

        double cmi = 0;
        foreach (var (_, s) in strata)
            cmi += (double)s.X.Count / n * MutualInformation(s.X, s.Y);
        return cmi;
    }

    /// <summary>Number of distinct labels present in a sample (the cardinality used for the G-test degrees of freedom).</summary>
    public static int DistinctCount(IReadOnlyList<int> labels)
    {
        var set = new HashSet<int>();
        foreach (var v in labels) set.Add(v);
        return set.Count;
    }
}
