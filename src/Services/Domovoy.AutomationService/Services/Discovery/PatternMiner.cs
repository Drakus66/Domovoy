using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Services.Discovery;

/// <summary>
/// The pure core of the pattern-discovery engine (roadmap Epic 2F) — the staged funnel that turns raw
/// event-log history into human-readable <c>IF sensor-condition THEN turn a device on</c> candidates. Kept a
/// pure static so every stage is unit-testable over synthetic streams, exactly as the 2C heuristic
/// (<see cref="RuleSuggester.Mine"/>) is; the <see cref="DiscoveryEngine"/> wraps this with I/O and queueing.
///
/// <para>The funnel (each stage cuts candidates before the next, costlier one):</para>
/// <list type="number">
///   <item><b>Stage 0 — align by transition.</b> History is bucketed into fixed time slots; each sensor is
///     sampled <i>as of the slot start</i> and each actuator's <b>human</b> on-actions are marked <i>within</i>
///     the slot — so the sensor state always precedes the action (this is what respects the arrow of time and
///     keeps a light's own power-draw from being mistaken for a cause).</item>
///   <item><b>Stage 1 — cheap screening.</b> For every sensor×actuator pair, score conditional mutual
///     information I(sensor; action | time-of-day) → a G-test p-value → Benjamini-Hochberg FDR across all
///     pairs. This is where confounders (a light and a sensor both tracking time-of-day) and random pairs die.</item>
///   <item><b>Stage 2 — hypothesis.</b> For a surviving pair, find the sensor condition (boolean "active", or a
///     numeric threshold for "dark → light") that best predicts the action, with support and confidence, and
///     optionally a time-of-day guard when it sharpens the pattern.</item>
///   <item><b>Stage 3 — validation.</b> Keep only patterns above support/confidence/lift thresholds; the label
///     is human choice only (<c>triggerSource=user</c> — no ML-on-ML, no self-fulfilling); a device is never
///     wired to itself.</item>
/// </list>
///
/// <para><b>Invariant (principle 1):</b> the miner only supplies hypotheses. The <see cref="DiscoveryEngine"/>
/// queues them as <c>Proposed</c> for human approval and staged rollout — nothing here activates anything.</para>
/// </summary>
public static class PatternMiner
{
    /// <summary>A mined candidate: a sensor condition (optionally time-guarded) that predicts a human turn-on.</summary>
    public sealed record DiscoveredPattern(
        string TriggerDeviceId,
        string TriggerCapability,
        string TriggerOperator,   // eq | lt | gt
        object TriggerValue,      // bool for eq; threshold (double) for lt/gt
        string ConditionText,     // human-readable trigger condition ("illuminance < 40")
        string ActionDeviceId,
        int Support,
        double Confidence,
        double BaseRate,
        double Lift,
        double MutualInfo,        // conditional MI in nats (the screening statistic)
        double PValue,
        string? FromTime,         // optional time-of-day guard (HH:mm), null if none
        string? ToTime);

    /// <summary>Run the full funnel over a chronological event stream. Returns the strongest patterns first.</summary>
    public static List<DiscoveredPattern> Mine(IReadOnlyList<DbGatewayClient.EventLogEntry> events, AutomationOptions options)
    {
        if (events.Count == 0) return new();

        var sensorCaps = new HashSet<string>(options.DiscoverySensorCapabilities, StringComparer.OrdinalIgnoreCase);
        var slot = TimeSpan.FromSeconds(Math.Max(30, options.DiscoverySlotSeconds));

        // ----- Stage 0: build the slot grid and the sensor / actuator series -----
        var from = events[0].Timestamp;
        var to = events[^1].Timestamp;
        var slotCount = (int)Math.Min(200_000, Math.Floor((to - from) / slot) + 1);
        if (slotCount < 4) return new(); // not enough span to say anything

        // Sensor series: (device, cap) → chronological (timestamp, numeric-or-truthy value) + whether it's numeric.
        var sensors = new Dictionary<(string Dev, string Cap), Series>();
        // Actuator series: device → chronological human on-action timestamps.
        var actuators = new Dictionary<string, List<DateTime>>();

        foreach (var e in events)
        {
            if (string.Equals(e.CapabilityId, CapabilityIds.OnOff, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(e.TriggerSource, "user", StringComparison.OrdinalIgnoreCase) && Truthy(e.NewValue))
                {
                    if (!actuators.TryGetValue(e.DeviceId, out var acts)) { acts = new(); actuators[e.DeviceId] = acts; }
                    acts.Add(e.Timestamp);
                }
                continue; // on_off is an action channel, never a candidate sensor (anti-loop)
            }

            if (!sensorCaps.Contains(e.CapabilityId)) continue;
            var key = (e.DeviceId, e.CapabilityId);
            if (!sensors.TryGetValue(key, out var s)) { s = new Series(); sensors[key] = s; }
            if (TryReadValue(e.NewValue, out var v, out var isNumeric))
            {
                s.Times.Add(e.Timestamp);
                s.Values.Add(v);
                s.Numeric |= isNumeric;
            }
        }

        if (sensors.Count == 0 || actuators.Count == 0) return new();

        // Time-of-day confounder per slot (bucket of 6h): the obvious thing a light and a sensor share.
        var slotStart = new DateTime[slotCount];
        var timeBucket = new int[slotCount];
        for (var i = 0; i < slotCount; i++)
        {
            slotStart[i] = from + TimeSpan.FromTicks(slot.Ticks * i);
            timeBucket[i] = slotStart[i].Hour / 6; // 0..3
        }

        // Per-actuator: does slot i contain a human on-action?
        var actionInSlot = new Dictionary<string, int[]>();
        foreach (var (dev, times) in actuators)
            actionInSlot[dev] = MarkSlots(times, from, slot, slotCount);

        // ----- Stage 1: screen every sensor × actuator pair by conditional MI → p-value → FDR -----
        var scored = new List<PairScore>();
        foreach (var (skey, series) in sensors)
        {
            var (asof, present) = SampleAsOf(series, slotStart);
            double t1, t2;
            var binned = series.Numeric
                ? Tertiles(series, asof, present, out t1, out t2, out _)
                : Boolean(asof, present, out t1, out t2);

            foreach (var (adev, y) in actionInSlot)
            {
                if (string.Equals(skey.Dev, adev, StringComparison.Ordinal)) continue; // never wire a device to itself

                // Align on slots where the sensor has a known value.
                var xs = new List<int>(); var ys = new List<int>(); var zs = new List<int>();
                for (var i = 0; i < slotCount; i++)
                {
                    if (!present[i]) continue;
                    xs.Add(binned[i]); ys.Add(y[i]); zs.Add(timeBucket[i]);
                }
                if (xs.Count < options.DiscoveryMinSupport * 2) continue;

                var nx = InformationTheory.DistinctCount(xs);
                var ny = InformationTheory.DistinctCount(ys);
                var nz = InformationTheory.DistinctCount(zs);
                if (nx < 2 || ny < 2) continue; // no variation ⇒ nothing to test

                var cmi = InformationTheory.ConditionalMutualInformation(xs, ys, zs);
                var df = Math.Max(1, nz * (nx - 1) * (ny - 1));
                var p = ChiSquared.SurvivalFunction(ChiSquared.GStatistic(cmi, xs.Count), df);

                scored.Add(new PairScore(skey.Dev, skey.Cap, adev, series.Numeric, t1, t2, xs, ys, zs, cmi, p));
            }
        }

        if (scored.Count == 0) return new();

        var accepted = Statistics.BenjaminiHochberg(scored.Select(s => s.PValue).ToList(), options.DiscoveryFdrQ);

        // ----- Stages 2 + 3: mine the condition, apply support/confidence/lift gates -----
        var patterns = new List<DiscoveredPattern>();
        for (var i = 0; i < scored.Count; i++)
        {
            if (!accepted[i]) continue;
            var pattern = MineCondition(scored[i], options);
            if (pattern is not null) patterns.Add(pattern);
        }

        return patterns
            .OrderByDescending(p => p.Lift)
            .ThenByDescending(p => p.Support)
            .Take(Math.Max(1, options.DiscoveryMaxProposals))
            .ToList();
    }

    // Stage 2/3 for one screened pair: pick the best predictive bin (+ optional time guard) and gate it.
    private static DiscoveredPattern? MineCondition(PairScore s, AutomationOptions options)
    {
        var n = s.X.Count;
        var baseRate = (double)s.Y.Count(v => v == 1) / n;
        if (baseRate <= 0) return null;

        // For a boolean sensor only the "active" bin (1) is meaningful; for a numeric one only the extreme
        // bins (0 = low, 2 = high) are expressible as a single threshold — the middle band is ambiguous.
        var candidateBins = s.Numeric ? new[] { 0, 2 } : new[] { 1 };

        DiscoveredPattern? best = null;
        foreach (var bin in candidateBins)
        {
            var idx = Enumerable.Range(0, n).Where(i => s.X[i] == bin).ToList();
            var support = idx.Count(i => s.Y[i] == 1);
            if (support < options.DiscoveryMinSupport) continue;

            var confidence = (double)support / idx.Count;
            if (confidence < options.DiscoveryMinConfidence) continue;

            var lift = confidence / baseRate;
            if (lift < options.DiscoveryMinLift) continue;

            // Optional time-of-day guard: does restricting to one 6h bucket sharpen the pattern materially?
            string? fromTime = null, toTime = null;
            var bestBucketConf = confidence; var bestSupport = support;
            foreach (var bucket in idx.Select(i => s.Z[i]).Distinct())
            {
                var bidx = idx.Where(i => s.Z[i] == bucket).ToList();
                var bSupport = bidx.Count(i => s.Y[i] == 1);
                if (bSupport < options.DiscoveryMinSupport) continue;
                var bConf = (double)bSupport / bidx.Count;
                if (bConf >= bestBucketConf + 0.15) // materially better, not noise
                {
                    bestBucketConf = bConf; bestSupport = bSupport;
                    (fromTime, toTime) = BucketWindow(bucket);
                }
            }
            if (fromTime is not null) { confidence = bestBucketConf; support = bestSupport; lift = confidence / baseRate; }

            var (op, value, text) = ConditionSpec(s, bin);
            var candidate = new DiscoveredPattern(
                s.SensorDev, s.SensorCap, op, value, text, s.ActuatorDev,
                support, Math.Round(confidence, 3), Math.Round(baseRate, 3), Math.Round(lift, 2),
                Math.Round(s.Cmi, 4), s.PValue, fromTime, toTime);

            if (best is null || candidate.Lift > best.Lift) best = candidate;
        }
        return best;
    }

    // Translate a chosen bin into a replayable trigger comparison + a human-readable condition string.
    private static (string Op, object Value, string Text) ConditionSpec(PairScore s, int bin)
    {
        if (!s.Numeric) return ("eq", true, $"{s.SensorCap} active");
        // Numeric: low bin ⇒ below the lower tertile ("dark"); high bin ⇒ above the upper tertile.
        return bin == 0
            ? ("lt", Math.Round(s.T1, 2), $"{s.SensorCap} < {Math.Round(s.T1, 2)}")
            : ("gt", Math.Round(s.T2, 2), $"{s.SensorCap} > {Math.Round(s.T2, 2)}");
    }

    private static (string From, string To) BucketWindow(int bucket)
    {
        var fromH = bucket * 6;
        var toH = fromH + 6;
        return ($"{fromH:00}:00", toH >= 24 ? "23:59" : $"{toH:00}:00");
    }

    // ----- Stage 0 helpers -----

    // A chronological sensor series; Numeric is set when any reading parsed as a number.
    private sealed class Series
    {
        public List<DateTime> Times { get; } = new();
        public List<double> Values { get; } = new();
        public bool Numeric { get; set; }
    }

    // Which screened pair, with the aligned label arrays and the tertile edges for numeric sensors.
    private sealed record PairScore(
        string SensorDev, string SensorCap, string ActuatorDev, bool Numeric, double T1, double T2,
        List<int> X, List<int> Y, List<int> Z, double Cmi, double PValue);

    // Mark, for each slot, whether any timestamp falls within it (two-pointer; timestamps sorted ascending).
    private static int[] MarkSlots(List<DateTime> times, DateTime from, TimeSpan slot, int slotCount)
    {
        var marks = new int[slotCount];
        foreach (var t in times)
        {
            var idx = (int)Math.Floor((t - from) / slot);
            if (idx >= 0 && idx < slotCount) marks[idx] = 1;
        }
        return marks;
    }

    // Sample the series "as of" each slot start: the last value at or before the slot start (state entering the slot).
    private static (double[] AsOf, bool[] Present) SampleAsOf(Series s, DateTime[] slotStart)
    {
        var asof = new double[slotStart.Length];
        var present = new bool[slotStart.Length];
        var ptr = 0;
        double last = 0; var have = false;
        for (var i = 0; i < slotStart.Length; i++)
        {
            while (ptr < s.Times.Count && s.Times[ptr] <= slotStart[i]) { last = s.Values[ptr]; have = true; ptr++; }
            asof[i] = last;
            present[i] = have;
        }
        return (asof, present);
    }

    // Boolean discretization: truthy → 1, else 0. (No thresholds.)
    private static int[] Boolean(double[] asof, bool[] present, out double t1, out double t2)
    {
        t1 = 0; t2 = 0;
        var bins = new int[asof.Length];
        for (var i = 0; i < asof.Length; i++) bins[i] = present[i] && asof[i] != 0 ? 1 : 0;
        return bins;
    }

    // Numeric discretization into tertiles (low/mid/high) over the present as-of values.
    private static int[] Tertiles(Series s, double[] asof, bool[] present, out double t1, out double t2, out int bins3)
    {
        var sample = new List<double>();
        for (var i = 0; i < asof.Length; i++) if (present[i]) sample.Add(asof[i]);
        sample.Sort();
        t1 = Quantile(sample, 1.0 / 3);
        t2 = Quantile(sample, 2.0 / 3);
        bins3 = 3;

        var bins = new int[asof.Length];
        for (var i = 0; i < asof.Length; i++)
            bins[i] = !present[i] ? 0 : asof[i] < t1 ? 0 : asof[i] < t2 ? 1 : 2;
        return bins;
    }

    private static double Quantile(List<double> sorted, double q)
    {
        if (sorted.Count == 0) return 0;
        var pos = q * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return lo == hi ? sorted[lo] : sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
    }

    // Read an event value as a number (numeric sensor) or a truthy flag (boolean sensor).
    private static bool TryReadValue(JsonElement? value, out double v, out bool isNumeric)
    {
        v = 0; isNumeric = false;
        if (value is not { } e) return false;
        switch (e.ValueKind)
        {
            case JsonValueKind.Number when e.TryGetDouble(out var d): v = d; isNumeric = true; return true;
            case JsonValueKind.True: v = 1; return true;
            case JsonValueKind.False: v = 0; return true;
            case JsonValueKind.String when bool.TryParse(e.GetString(), out var b): v = b ? 1 : 0; return true;
            case JsonValueKind.String when double.TryParse(e.GetString(),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dp):
                v = dp; isNumeric = true; return true;
            default: return false;
        }
    }

    private static bool Truthy(JsonElement? value) => value is { } e && e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.Number => e.TryGetDouble(out var d) && d != 0,
        JsonValueKind.String => bool.TryParse(e.GetString(), out var b) && b,
        _ => false,
    };
}
