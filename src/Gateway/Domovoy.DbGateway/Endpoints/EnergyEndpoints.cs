// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Stores;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Energy-accounting read endpoints (roadmap Epic 3C, Etap 1). Turns the raw cumulative <c>energy</c>
/// telemetry into per-device / per-zone kWh consumption over a window and honest totals, reusing the
/// on-the-fly rollups in <see cref="HistoryEndpoints"/> (no separate rollup store). The ApiGateway forwards
/// to these via its EnergyController; the WebUI energy dashboard (Etap 6) renders the result.
/// </summary>
public static class EnergyEndpoints
{
    /// <summary>Energy roles a user can pin on a device to keep totals honest (Epic 3C). <c>consumer</c> (or
    /// null) ⇒ a normal load that counts toward the per-device sum; <c>mains</c> ⇒ a whole-home/aggregate meter
    /// surfaced as the grand total but excluded from that sum (no double count). Leaving a device out of the
    /// totals entirely is the accounting toggle (<c>EnergyProfile.Track = false</c>), not a role.</summary>
    public static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { MainsRole, ConsumerRole };

    public const string MainsRole = "mains";
    public const string ConsumerRole = "consumer";

    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    /// <summary>Per-device consumption over the window (kWh) with its zone/archetype/role for grouping/ranking.</summary>
    public record EnergyDeviceConsumption(
        string DeviceId, string Name, string ZoneId, string? Archetype, string? EnergyRole, double Kwh);

    /// <summary>Consumption for every energy-metered device plus honest totals (Epic 3C energy accounting).</summary>
    public record EnergyConsumptionResult(
        DateTime From, DateTime To, string Bucket,
        IReadOnlyList<EnergyDeviceConsumption> Devices, double ConsumerTotalKwh, double MainsTotalKwh);

    /// <summary>Consumption + cost for one tariff zone over the window (Epic 3C money breakdown).</summary>
    public record EnergyCostZone(string Zone, double Kwh, double Cost);

    /// <summary>Household energy cost over the window, priced by the tariff (Epic 3C).</summary>
    public record EnergyCostResult(
        DateTime From, DateTime To, string Currency, double TotalKwh, double TotalCost,
        IReadOnlyList<EnergyCostZone> Zones);

    /// <summary>
    /// One node of the electrical tree with what its subtree consumed/draws (Epic 3C-D). When the node has its
    /// own meter, <see cref="MeterKwh"/> is what it actually measured and <see cref="UnaccountedKwh"/> is the
    /// part no known device explains — the honest "everything else on this line" figure.
    /// </summary>
    public record EnergyNodeBreakdown(
        string NodeId, string Name, string Kind, string? ParentId, string? Phase,
        double Kwh, double PowerW, int DeviceCount,
        double? MeterKwh, double? MeterPowerW, double? UnaccountedKwh, double? LimitWatts);

    /// <summary>Consumption and live draw on one phase (Epic 3C-D) — the imbalance view of a 3-phase intake.</summary>
    public record EnergyPhaseBreakdown(string Phase, double Kwh, double PowerW, double? LimitWatts);

    /// <summary>
    /// Consumption/draw broken down over the electrical topology (Epic 3C-D). <see cref="UnmappedKwh"/> covers
    /// the tracked devices that are not attached to any circuit yet — they are counted in the household totals
    /// but cannot be attributed to a line or a phase.
    /// </summary>
    public record EnergyBreakdownResult(
        DateTime From, DateTime To,
        IReadOnlyList<EnergyNodeBreakdown> Nodes, IReadOnlyList<EnergyPhaseBreakdown> Phases,
        double UnmappedKwh, double UnmappedPowerW);

    /// <summary>Assumed line voltage when a node doesn't state one — turns a breaker rating into watts.</summary>
    private const double DefaultVoltage = 230;

    public static void MapEnergyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/energy").WithTags("Energy").WithOpenApi();

        // GET /api/energy/consumption?from=&to=&bucket=hour — per-device kWh + totals for the window.
        group.MapGet("/consumption", async (DateTime? from, DateTime? to, string? bucket, IMongoDatabase db, ITelemetryStore store) =>
        {
            try { return Results.Ok(await ConsumptionAsync(db, store, from, to, bucket)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // GET /api/energy/cost?from=&to= — total household kWh + money for the window, broken down by tariff zone.
        group.MapGet("/cost", async (DateTime? from, DateTime? to, IMongoDatabase db, ITelemetryStore store) =>
        {
            try { return Results.Ok(await CostAsync(db, store, from, to)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // GET /api/energy/breakdown?from=&to= — consumption/draw per topology node and per phase (Epic 3C-D).
        group.MapGet("/breakdown", async (DateTime? from, DateTime? to, IMongoDatabase db, ITelemetryStore store) =>
        {
            try { return Results.Ok(await BreakdownAsync(db, store, from, to)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }

    /// <summary>
    /// Compute per-device energy consumption over the window from the cumulative <c>energy</c> series
    /// (roadmap Epic 3C, Etap 1). Consumption is the sum of per-bucket <c>delta</c>s (reset-aware, reusing
    /// <see cref="HistoryEndpoints.AggregateBatchAsync"/>), so multiple counter resets in the window are handled.
    /// Devices pinned <c>mains</c> feed the grand total only; a device whose accounting toggle is off never
    /// reaches this result at all. Extracted from the endpoint so it is integration-testable against the store.
    /// </summary>
    public static async Task<EnergyConsumptionResult> ConsumptionAsync(
        IMongoDatabase db, ITelemetryStore store, DateTime? from, DateTime? to, string? bucket)
    {
        var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;
        var bucketName = string.IsNullOrEmpty(bucket) ? "hour" : bucket;

        var devices = await TrackedDevicesAsync(db);

        var kwhByDevice = new Dictionary<string, double>(StringComparer.Ordinal);
        if (devices.Count > 0)
        {
            var specs = devices.Select(d => new SeriesSpec(d.Id, CapabilityIds.Energy)).ToList();
            // maxPoints is clamped to the store cap; the sum is over every bucket, so the whole window counts.
            var series = await store.AggregateBatchAsync(
                specs, lo, hi, bucketName, "delta", int.MaxValue, default);
            foreach (var s in series)
                kwhByDevice[s.DeviceId] = Math.Max(0, s.Buckets.Sum(b => b.Value));
        }

        var rows = devices
            .Select(d =>
            {
                var role = RoleOf(d);
                var archetype = string.IsNullOrEmpty(d.Archetype) ? d.AutoArchetype : d.Archetype;
                var kwh = kwhByDevice.TryGetValue(d.Id, out var v) ? Math.Round(v, 3) : 0;
                return new EnergyDeviceConsumption(d.Id, d.Name, d.ZoneId, archetype, role, kwh);
            })
            .OrderByDescending(r => r.Kwh)  // Top Consumers first
            .ToList();

        var consumerTotal = rows.Where(r => r.EnergyRole != MainsRole).Sum(r => r.Kwh);
        var mainsTotal = rows.Where(r => r.EnergyRole == MainsRole).Sum(r => r.Kwh);

        return new EnergyConsumptionResult(
            lo, hi, bucketName, rows, Math.Round(consumerTotal, 3), Math.Round(mainsTotal, 3));
    }

    /// <summary>
    /// Consumption and live draw projected onto the electrical topology (roadmap Epic 3C-D): per node (supply /
    /// panel / circuit, each including its whole subtree) and per phase. A node with its own meter also reports
    /// what that meter measured and the <b>unaccounted</b> remainder — the load on the line that no configured
    /// device explains, which is the honest way to show an incompletely mapped house. Devices not attached to a
    /// circuit are reported separately rather than silently folded into a line.
    /// Pure over the store → integration-testable, like <see cref="ConsumptionAsync"/>.
    /// </summary>
    public static async Task<EnergyBreakdownResult> BreakdownAsync(IMongoDatabase db, ITelemetryStore store, DateTime? from, DateTime? to)
    {
        var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;

        var nodes = await db.GetCollection<PowerNode>(PowerTopologyEndpoints.Collection)
            .Find(FilterDefinition<PowerNode>.Empty).ToListAsync();
        var devices = await TrackedDevicesAsync(db);

        // kWh over the window for every tracked device AND every node meter (a meter may be pinned `mains`,
        // which keeps it out of the per-device sums but is exactly what the balance check needs).
        var meterIds = nodes.Where(n => !string.IsNullOrWhiteSpace(n.MeterDeviceId))
            .Select(n => n.MeterDeviceId!).ToHashSet(StringComparer.Ordinal);
        var seriesIds = devices.Select(d => d.Id).Concat(meterIds).Distinct(StringComparer.Ordinal).ToList();

        var kwhById = new Dictionary<string, double>(StringComparer.Ordinal);
        if (seriesIds.Count > 0)
        {
            var specs = seriesIds.Select(id => new SeriesSpec(id, CapabilityIds.Energy)).ToList();
            var series = await store.AggregateBatchAsync(
                specs, lo, hi, "hour", "delta", int.MaxValue, default);
            foreach (var s in series)
                kwhById[s.DeviceId] = Math.Max(0, s.Buckets.Sum(b => b.Value));
        }

        // Live draw comes from the read-model rather than telemetry: it is the current value by definition.
        var powerById = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var d in devices) powerById[d.Id] = CurrentPowerW(d);
        if (meterIds.Count > 0)
        {
            var meterDocs = await db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection)
                .Find(Builders<CapabilityDeviceDocument>.Filter.In(x => x.Id, meterIds)).ToListAsync();
            foreach (var m in meterDocs) powerById[m.Id] = CurrentPowerW(m);
        }

        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var childrenOf = nodes.Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Only real consumers are attributed to lines; an aggregate meter measures the line, it isn't a load on it.
        var ownLoads = devices.Where(d => RoleOf(d) != MainsRole)
            .GroupBy(d => d.EnergyProfile?.CircuitId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var rows = new List<EnergyNodeBreakdown>();
        var phaseKwh = new Dictionary<string, double>(StringComparer.Ordinal);
        var phasePower = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var node in nodes.OrderBy(n => n.Order).ThenBy(n => n.Name, StringComparer.Ordinal))
        {
            var subtree = Subtree(node, childrenOf).ToList();
            var loads = subtree.SelectMany(n => ownLoads.TryGetValue(n.Id, out var l) ? l : new List<CapabilityDeviceDocument>()).ToList();

            var kwh = loads.Sum(d => kwhById.TryGetValue(d.Id, out var v) ? v : 0);
            var powerW = loads.Sum(d => powerById.TryGetValue(d.Id, out var v) ? v : 0);

            double? meterKwh = null, meterPowerW = null, unaccounted = null;
            if (node.MeterDeviceId is { } meterId)
            {
                meterKwh = kwhById.TryGetValue(meterId, out var mk) ? Math.Round(mk, 3) : 0;
                meterPowerW = powerById.TryGetValue(meterId, out var mp) ? Math.Round(mp, 1) : 0;
                // Negative would mean the devices "consumed" more than the meter saw — a mapping error, not a
                // negative load; clamp so the figure stays readable and let the UI flag the mismatch.
                unaccounted = Math.Round(Math.Max(0, meterKwh.Value - kwh), 3);
            }

            rows.Add(new EnergyNodeBreakdown(
                node.Id, node.Name, node.Kind, node.ParentId, PhaseOf(node, byId),
                Math.Round(kwh, 3), Math.Round(powerW, 1), loads.Count,
                meterKwh, meterPowerW, unaccounted, LimitWattsOf(node)));
        }

        // Per-phase totals: every consumer contributes to the phase of its circuit; a three-phase line spreads
        // evenly over L1/L2/L3 (v1 has no per-phase metering of a single load).
        foreach (var device in devices.Where(d => RoleOf(d) != MainsRole))
        {
            var circuitId = device.EnergyProfile?.CircuitId;
            if (circuitId is null || !byId.TryGetValue(circuitId, out var node)) continue;
            if (PhaseOf(node, byId) is not { } phase) continue;

            var kwh = kwhById.TryGetValue(device.Id, out var k) ? k : 0;
            var powerW = powerById.TryGetValue(device.Id, out var p) ? p : 0;
            var targets = phase == PowerPhases.Three ? PowerPhases.Single : new[] { phase };
            var share = phase == PowerPhases.Three ? 1.0 / PowerPhases.Single.Count : 1.0;

            foreach (var target in targets)
            {
                phaseKwh[target] = (phaseKwh.TryGetValue(target, out var pk) ? pk : 0) + kwh * share;
                phasePower[target] = (phasePower.TryGetValue(target, out var pp) ? pp : 0) + powerW * share;
            }
        }

        var phases = PowerPhases.Single
            .Where(p => phaseKwh.ContainsKey(p) || phasePower.ContainsKey(p))
            .Select(p => new EnergyPhaseBreakdown(
                p,
                Math.Round(phaseKwh.TryGetValue(p, out var k) ? k : 0, 3),
                Math.Round(phasePower.TryGetValue(p, out var w) ? w : 0, 1),
                PhaseLimitWatts(nodes, byId, p)))
            .ToList();

        var unmapped = devices
            .Where(d => RoleOf(d) != MainsRole)
            .Where(d => d.EnergyProfile?.CircuitId is not { } id || !byId.ContainsKey(id))
            .ToList();

        return new EnergyBreakdownResult(
            lo, hi, rows, phases,
            Math.Round(unmapped.Sum(d => kwhById.TryGetValue(d.Id, out var v) ? v : 0), 3),
            Math.Round(unmapped.Sum(d => powerById.TryGetValue(d.Id, out var v) ? v : 0), 1));
    }

    /// <summary>A node and every node below it (the tree is small; a plain walk is enough).</summary>
    private static IEnumerable<PowerNode> Subtree(PowerNode root, IReadOnlyDictionary<string, List<PowerNode>> childrenOf)
    {
        yield return root;
        if (!childrenOf.TryGetValue(root.Id, out var children)) yield break;
        foreach (var child in children)
            foreach (var descendant in Subtree(child, childrenOf))
                yield return descendant;
    }

    /// <summary>The node's phase, inherited from the nearest ancestor that states one (null ⇒ unknown).</summary>
    private static string? PhaseOf(PowerNode node, IReadOnlyDictionary<string, PowerNode> byId)
    {
        var current = node;
        var guard = 0;
        while (guard++ < 32)
        {
            if (!string.IsNullOrWhiteSpace(current.Phase)) return current.Phase;
            if (current.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent)) return null;
            current = parent;
        }
        return null; // a cycle in a hand-edited topology must not hang the request
    }

    /// <summary>Breaker rating turned into watts (A × V); null when the node states no rating.</summary>
    private static double? LimitWattsOf(PowerNode node) =>
        node.BreakerAmps is { } amps and > 0 ? amps * (node.Voltage is { } v and > 0 ? v : DefaultVoltage) : null;

    /// <summary>
    /// The limit for a phase: the rating of the highest node carrying it (the intake breaker), or for a
    /// three-phase node its per-phase share. Null when nothing on that phase is rated.
    /// </summary>
    private static double? PhaseLimitWatts(
        IReadOnlyList<PowerNode> nodes, IReadOnlyDictionary<string, PowerNode> byId, string phase)
    {
        double? best = null;
        foreach (var node in nodes)
        {
            if (LimitWattsOf(node) is not { } limit) continue;
            var nodePhase = PhaseOf(node, byId);
            if (nodePhase != phase && nodePhase != PowerPhases.Three) continue;
            if (best is null || limit > best) best = limit;
        }
        return best;
    }

    /// <summary>Live draw (W) from the read-model state; 0 when the device reports none.</summary>
    private static double CurrentPowerW(CapabilityDeviceDocument device) =>
        device.State.TryGetValue(CapabilityIds.Power, out var raw) && TryDouble(raw, out var w) ? Math.Max(0, w) : 0;

    private static bool TryDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case decimal m: result = (double)m; return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var p): result = p; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>Coerce a stored role to the allow-list; unknown/blank/<c>consumer</c> ⇒ null (a normal load).</summary>
    public static string? NormalizeRole(string? role) =>
        !string.IsNullOrWhiteSpace(role) && Roles.Contains(role) && role != ConsumerRole ? role : null;

    /// <summary>The device's effective energy role — <c>mains</c> or null (a normal consumer).</summary>
    public static string? RoleOf(CapabilityDeviceDocument device) => NormalizeRole(device.EnergyProfile?.Role);

    /// <summary>True when the device itself reports a cumulative <c>energy</c> counter (its adapter's, or the
    /// synthetic one the platform maintains for a tracked device — either way the series exists).</summary>
    public static bool IsMetered(CapabilityDeviceDocument device) =>
        device.Capabilities.Any(c => c.Id == CapabilityIds.Energy);

    /// <summary>Whether the device counts toward kWh totals: the explicit accounting toggle when set, otherwise
    /// the default — a metered device counts, an unmetered one does not (Epic 3C-D).</summary>
    public static bool CountsTowardTotals(CapabilityDeviceDocument device) =>
        device.EnergyProfile?.Track ?? IsMetered(device);

    /// <summary>
    /// Devices whose consumption enters the accounting. The store-side filter is a superset (an energy
    /// capability <b>or</b> the accounting toggle — a freshly-tracked device gets its synthetic capability only
    /// on the next announce), narrowed in memory by <see cref="CountsTowardTotals"/>.
    /// </summary>
    public static async Task<List<CapabilityDeviceDocument>> TrackedDevicesAsync(IMongoDatabase db)
    {
        var f = Builders<CapabilityDeviceDocument>.Filter;
        var candidates = await db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection)
            .Find(f.Or(
                f.ElemMatch(x => x.Capabilities, c => c.Id == CapabilityIds.Energy),
                f.Eq(x => x.EnergyProfile!.Track, true)))
            .ToListAsync();
        return candidates.Where(CountsTowardTotals).ToList();
    }

    /// <summary>
    /// Household energy cost over the window (roadmap Epic 3C, Etap 3). Sums the per-hour consumption of the
    /// <b>consumer</b> devices (role null — mains/excluded don't enter the household bill), prices each hour by
    /// the tariff zone active at that hour (in the site's local time), and breaks the money down by zone.
    /// Reuses the reset-aware hourly <c>delta</c> rollup; pure over the store → integration-testable.
    /// </summary>
    public static async Task<EnergyCostResult> CostAsync(IMongoDatabase db, ITelemetryStore store, DateTime? from, DateTime? to)
    {
        var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;

        var tariff = await db.GetCollection<TariffSettings>(SettingsEndpoints.TariffCollection)
            .Find(x => x.Id == TariffSettings.SingletonId).FirstOrDefaultAsync() ?? new TariffSettings();
        var location = await db.GetCollection<SiteLocation>(SettingsEndpoints.Collection)
            .Find(x => x.Id == SiteLocation.SingletonId).FirstOrDefaultAsync();
        var tz = ResolveTimeZone(location?.TimeZoneId);

        var devices = await TrackedDevicesAsync(db);
        var consumers = devices.Where(d => RoleOf(d) is null).ToList();

        var zoneAgg = new Dictionary<string, (double Kwh, double Cost)>(StringComparer.Ordinal);
        double totalKwh = 0, totalCost = 0;

        if (consumers.Count > 0)
        {
            var specs = consumers.Select(d => new SeriesSpec(d.Id, CapabilityIds.Energy)).ToList();
            var series = await store.AggregateBatchAsync(
                specs, lo, hi, "hour", "delta", int.MaxValue, default);

            // Sum per-hour consumption across all consumer devices, then price each hour by its local tariff zone.
            var hourly = new Dictionary<DateTime, double>();
            foreach (var s in series)
                foreach (var b in s.Buckets)
                    hourly[b.Timestamp] = (hourly.TryGetValue(b.Timestamp, out var acc) ? acc : 0) + Math.Max(0, b.Value);

            foreach (var (hourUtc, kwh) in hourly)
            {
                var local = TimeZoneInfo.ConvertTime(
                    new DateTimeOffset(DateTime.SpecifyKind(hourUtc, DateTimeKind.Utc)), tz);
                var (zone, price) = TariffCalculator.At(tariff, local);
                var cost = kwh * price;
                totalKwh += kwh;
                totalCost += cost;
                var prev = zoneAgg.TryGetValue(zone, out var z) ? z : (Kwh: 0d, Cost: 0d);
                zoneAgg[zone] = (prev.Kwh + kwh, prev.Cost + cost);
            }
        }

        var zones = zoneAgg
            .Select(kv => new EnergyCostZone(kv.Key, Math.Round(kv.Value.Kwh, 3), Math.Round(kv.Value.Cost, 2)))
            .OrderByDescending(z => z.Cost)
            .ToList();

        return new EnergyCostResult(
            lo, hi, tariff.Currency, Math.Round(totalKwh, 3), Math.Round(totalCost, 2), zones);
    }

    /// <summary>Общее правило площадки: неизвестный/пустой идентификатор ⇒ UTC (деньги считаются и офлайн).</summary>
    private static TimeZoneInfo ResolveTimeZone(string? ianaId) => SiteTimeZone.Resolve(ianaId);
}
