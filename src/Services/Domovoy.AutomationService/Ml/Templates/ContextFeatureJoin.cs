namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Server-side context-join (roadmap Epic 2B/2I) — the prerequisite the roadmap named for enriching ML features
/// with context. It attaches the home mode in effect at each training row's timestamp by an <b>as-of</b> join
/// against the mode timeline (from the P0-5 event-log / 1G <c>mode_change</c> records): the last mode change at
/// or before the row. Home mode is an <b>ambient</b> feature (<see cref="FeatureLocality"/>) — it influences every
/// zone — so it feeds every model. Pure and unit-testable.
/// </summary>
public static class ContextFeatureJoin
{
    /// <summary>The mode in effect at <paramref name="at"/>: the last change at or before it, or null if none precedes it.</summary>
    public static string? ModeAt(DateTime at, IReadOnlyList<(DateTime At, string Mode)> timeline)
    {
        string? current = null;
        // Timeline is chronological; walk to the last entry not after `at` (linear — mode changes are rare).
        foreach (var (t, mode) in timeline)
        {
            if (t > at) break;
            current = mode;
        }
        return current;
    }

    /// <summary>Return the samples with <see cref="LabeledSample.Mode"/> filled from the mode timeline (as-of).</summary>
    public static List<LabeledSample> WithMode(
        IReadOnlyList<LabeledSample> samples, IReadOnlyList<(DateTime At, string Mode)> timeline)
    {
        if (timeline.Count == 0) return samples.ToList();
        var ordered = timeline.OrderBy(x => x.At).ToList();
        return samples.Select(s => s with { Mode = ModeAt(s.Timestamp, ordered) ?? s.Mode }).ToList();
    }
}
