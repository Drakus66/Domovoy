// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using MongoDB.Bson;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Stores.Mongo;

/// <summary>
/// MongoDB implementation of <see cref="ITelemetryStore"/> (roadmap Epic 3H seam Ф1). All the storage specifics —
/// time-series collections (<see cref="TimeSeriesInitializer"/>), <c>$dateTrunc</c> rollups, <c>$group</c>
/// accumulators, counter-reset-aware delta — live here, behind the domain interface. Moving a backend (PostgreSQL,
/// Ф2) means writing a sibling implementation; <see cref="Endpoints.HistoryEndpoints"/> and the rest of the
/// gateway never see a <c>BsonDocument</c> or <c>FilterDefinition</c>.
/// </summary>
public sealed class MongoTelemetryStore : ITelemetryStore
{
    private const int DefaultLimit = 500;
    private const int MaxLimit = 5000;
    private const int MaxBatchItems = 200;
    private const int DefaultMaxPoints = 48;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);
    private const string AggError = "agg must be avg, min, max, sum or delta";

    // Bucket size → MongoDB $dateTrunc unit. Closed allow-list (no injection).
    private static readonly Dictionary<string, string> BucketUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minute"] = "minute", ["hour"] = "hour", ["day"] = "day",
    };

    private readonly IMongoDatabase _db;

    public MongoTelemetryStore(IMongoDatabase db) => _db = db;

    private IMongoCollection<DeviceEventLog> Events =>
        _db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection);
    private IMongoCollection<SensorReading> Readings =>
        _db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection);

    public async Task<IReadOnlyList<EventLogRow>> QueryEventsAsync(
        string? deviceId, string? capabilityId, string? zoneId, string? kind,
        DateTime? from, DateTime? to, int? limit, CancellationToken ct)
    {
        var (lo, hi, take) = Window(from, to, limit);
        var b = Builders<DeviceEventLog>.Filter;
        var filters = new List<FilterDefinition<DeviceEventLog>> { b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi) };
        if (!string.IsNullOrEmpty(deviceId)) filters.Add(b.Eq(x => x.Meta.DeviceId, deviceId));
        if (!string.IsNullOrEmpty(zoneId)) filters.Add(b.Eq(x => x.Meta.ZoneId, zoneId));
        if (!string.IsNullOrEmpty(kind)) filters.Add(b.Eq(x => x.Meta.Kind, kind));
        if (!string.IsNullOrEmpty(capabilityId)) filters.Add(b.Eq(x => x.CapabilityId, capabilityId));

        var docs = await Events.Find(b.And(filters)).SortByDescending(x => x.Timestamp).Limit(take).ToListAsync(ct);
        return docs.Select(d => new EventLogRow(
            d.Timestamp, d.Meta.DeviceId, d.Meta.ZoneId, d.Meta.Kind, d.CapabilityId,
            d.OldValue, d.NewValue, d.TriggerSource, d.TriggerId, d.RuleId, d.DecisionId, d.Mode, d.CorrelationId)).ToList();
    }

    public async Task<IReadOnlyList<TelemetryRow>> QueryTelemetryAsync(
        string? deviceId, string? capabilityId, string? zoneId,
        DateTime? from, DateTime? to, int? limit, CancellationToken ct)
    {
        var (lo, hi, take) = Window(from, to, limit);
        var b = Builders<SensorReading>.Filter;
        var filters = new List<FilterDefinition<SensorReading>> { b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi) };
        if (!string.IsNullOrEmpty(deviceId)) filters.Add(b.Eq(x => x.Meta.DeviceId, deviceId));
        if (!string.IsNullOrEmpty(zoneId)) filters.Add(b.Eq(x => x.Meta.ZoneId, zoneId));
        if (!string.IsNullOrEmpty(capabilityId)) filters.Add(b.Eq(x => x.Meta.CapabilityId, capabilityId));

        var docs = await Readings.Find(b.And(filters)).SortByDescending(x => x.Timestamp).Limit(take).ToListAsync(ct);
        return docs.Select(d => new TelemetryRow(
            d.Timestamp, d.Meta.DeviceId, d.Meta.ZoneId, d.Meta.CapabilityId, d.Meta.Unit, d.Value)).ToList();
    }

    public Task<long> CountTelemetryAsync(string? capabilityId, string? zoneId, DateTime? from, DateTime? to, CancellationToken ct) =>
        Readings.CountDocumentsAsync(TelemetryFilter(capabilityId, zoneId, from, to), cancellationToken: ct);

    public async Task<IReadOnlyList<ZoneCount>> CountTelemetryByZoneAsync(string? capabilityId, DateTime? from, DateTime? to, CancellationToken ct) =>
        await CountByZone(Readings, TelemetryFilter(capabilityId, null, from, to), ct);

    public Task<long> CountEventsAsync(string? capabilityId, string? zoneId, DateTime? from, DateTime? to, CancellationToken ct) =>
        Events.CountDocumentsAsync(EventFilter(capabilityId, zoneId, from, to), cancellationToken: ct);

    public async Task<IReadOnlyList<ZoneCount>> CountEventsByZoneAsync(string? capabilityId, DateTime? from, DateTime? to, CancellationToken ct) =>
        await CountByZone(Events, EventFilter(capabilityId, null, from, to), ct);

    public async Task<DateTime?> EarliestEventAsync(CancellationToken ct)
    {
        // Deliberately not windowed: the true oldest event over the whole collection (3I cold-start gate).
        var oldest = await Events.Find(FilterDefinition<DeviceEventLog>.Empty)
            .SortBy(x => x.Timestamp).Limit(1).FirstOrDefaultAsync(ct);
        return oldest?.Timestamp;
    }

    public async Task<IReadOnlyList<AggregateBucket>> AggregateTelemetryAsync(
        string? deviceId, string? capabilityId, string? zoneId,
        DateTime? from, DateTime? to, string? bucket, string? agg, CancellationToken ct)
    {
        if (!BucketUnits.TryGetValue(bucket ?? "hour", out var unit))
            throw new ArgumentException("bucket must be minute, hour or day");
        var which = (agg ?? "avg").ToLowerInvariant();
        if (!AggAllowed(which)) throw new ArgumentException(AggError);

        var (lo, hi, _) = Window(from, to, null);
        var match = new BsonDocument { { "Timestamp", new BsonDocument { { "$gte", lo }, { "$lte", hi } } } };
        if (!string.IsNullOrEmpty(deviceId)) match["Meta.DeviceId"] = deviceId;
        if (!string.IsNullOrEmpty(zoneId)) match["Meta.ZoneId"] = zoneId;
        if (!string.IsNullOrEmpty(capabilityId)) match["Meta.CapabilityId"] = capabilityId;

        var stages = new List<BsonDocument> { new("$match", match) };
        if (which == "delta") stages.Add(new BsonDocument("$sort", new BsonDocument("Timestamp", 1)));
        stages.Add(new BsonDocument("$group", GroupStage(new BsonDocument("$dateTrunc", new BsonDocument
            { { "date", "$Timestamp" }, { "unit", unit }, { "binSize", 1 } }))));
        stages.Add(new BsonDocument("$sort", new BsonDocument("_id", 1)));
        stages.Add(new BsonDocument("$limit", MaxLimit));

        var rows = await Readings.Aggregate<BsonDocument>(stages.ToArray(), cancellationToken: ct).ToListAsync(ct);
        return rows.Select(r => BucketOf(r, r["_id"].ToUniversalTime(), which)).ToList();
    }

    public async Task<IReadOnlyList<SeriesResult>> AggregateBatchAsync(
        IReadOnlyList<SeriesSpec>? series, DateTime? from, DateTime? to, string? bucket, string? agg, int? maxPoints, CancellationToken ct)
    {
        if (!BucketUnits.TryGetValue(bucket ?? "hour", out var unit))
            throw new ArgumentException("bucket must be minute, hour or day");
        var which = (agg ?? "avg").ToLowerInvariant();
        if (!AggAllowed(which)) throw new ArgumentException(AggError);

        var specs = (series ?? Array.Empty<SeriesSpec>())
            .Where(s => !string.IsNullOrEmpty(s.DeviceId) && !string.IsNullOrEmpty(s.CapabilityId))
            .Distinct().ToList();
        if (specs.Count > MaxBatchItems) throw new ArgumentException($"at most {MaxBatchItems} series per request");
        if (specs.Count == 0) return new List<SeriesResult>();

        var cap = Math.Clamp(maxPoints ?? DefaultMaxPoints, 1, MaxLimit);
        var (lo, hi, _) = Window(from, to, null);

        var orConds = new BsonArray(specs.Select(s => new BsonDocument
            { { "Meta.DeviceId", s.DeviceId }, { "Meta.CapabilityId", s.CapabilityId } }));

        var stages = new List<BsonDocument>
        {
            new("$match", new BsonDocument
            {
                { "Timestamp", new BsonDocument { { "$gte", lo }, { "$lte", hi } } },
                { "$or", orConds },
            }),
        };
        if (which == "delta") stages.Add(new BsonDocument("$sort", new BsonDocument("Timestamp", 1)));
        stages.Add(new BsonDocument("$group", GroupStage(new BsonDocument
        {
            { "d", "$Meta.DeviceId" },
            { "c", "$Meta.CapabilityId" },
            { "t", new BsonDocument("$dateTrunc", new BsonDocument
                { { "date", "$Timestamp" }, { "unit", unit }, { "binSize", 1 } }) },
        })));
        stages.Add(new BsonDocument("$sort", new BsonDocument { { "_id.d", 1 }, { "_id.c", 1 }, { "_id.t", 1 } }));
        stages.Add(new BsonDocument("$limit", MaxLimit));

        var rows = await Readings.Aggregate<BsonDocument>(stages.ToArray(), cancellationToken: ct).ToListAsync(ct);

        var byKey = new Dictionary<(string, string), List<AggregateBucket>>();
        foreach (var r in rows)
        {
            var id = r["_id"].AsBsonDocument;
            var key = (id["d"].AsString, id["c"].AsString);
            if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<AggregateBucket>();
            list.Add(BucketOf(r, id["t"].ToUniversalTime(), which));
        }

        return specs.Select(s =>
        {
            var list = byKey.TryGetValue((s.DeviceId, s.CapabilityId), out var l)
                ? (l.Count > cap ? l.GetRange(l.Count - cap, cap) : l)
                : new List<AggregateBucket>();
            return new SeriesResult(s.DeviceId, s.CapabilityId, list);
        }).ToList();
    }

    public async Task<IReadOnlyList<LatestEventRow>> LatestByDeviceAsync(
        IReadOnlyList<string>? deviceIds, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var ids = (deviceIds ?? Array.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (ids.Count > MaxBatchItems) throw new ArgumentException($"at most {MaxBatchItems} devices per request");
        if (ids.Count == 0) return new List<LatestEventRow>();

        var (lo, hi, _) = Window(from, to, null);
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "Timestamp", new BsonDocument { { "$gte", lo }, { "$lte", hi } } },
                { "Meta.DeviceId", new BsonDocument("$in", new BsonArray(ids)) },
            }),
            new BsonDocument("$sort", new BsonDocument("Timestamp", -1)),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$Meta.DeviceId" },
                { "Timestamp", new BsonDocument("$first", "$Timestamp") },
                { "CapabilityId", new BsonDocument("$first", "$CapabilityId") },
                { "TriggerSource", new BsonDocument("$first", "$TriggerSource") },
                { "TriggerId", new BsonDocument("$first", "$TriggerId") },
                { "RuleId", new BsonDocument("$first", "$RuleId") },
                { "CorrelationId", new BsonDocument("$first", "$CorrelationId") },
                { "NewValue", new BsonDocument("$first", "$NewValue") },
            }),
        };

        var rows = await Events.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        string? Str(BsonDocument d, string f) => d.TryGetValue(f, out var v) && !v.IsBsonNull ? v.AsString : null;

        return rows.Select(r => new LatestEventRow(
            r["_id"].AsString,
            r["Timestamp"].ToUniversalTime(),
            Str(r, "CapabilityId") ?? string.Empty,
            Str(r, "TriggerSource") ?? string.Empty,
            Str(r, "TriggerId"),
            Str(r, "RuleId"),
            Str(r, "CorrelationId"),
            r.TryGetValue("NewValue", out var nv) && !nv.IsBsonNull ? BsonTypeMapper.MapToDotNetValue(nv) : null)).ToList();
    }

    private static bool AggAllowed(string agg) => agg is "avg" or "min" or "max" or "sum" or "delta";

    private static BsonDocument GroupStage(BsonValue id) => new()
    {
        { "_id", id },
        { "avg", new BsonDocument("$avg", "$Value") },
        { "min", new BsonDocument("$min", "$Value") },
        { "max", new BsonDocument("$max", "$Value") },
        { "sum", new BsonDocument("$sum", "$Value") },
        { "first", new BsonDocument("$first", "$Value") },
        { "last", new BsonDocument("$last", "$Value") },
        { "count", new BsonDocument("$sum", 1) },
    };

    private static AggregateBucket BucketOf(BsonDocument r, DateTime ts, string which)
    {
        var avg = r["avg"].ToDouble();
        var min = r["min"].ToDouble();
        var max = r["max"].ToDouble();
        var sum = r["sum"].ToDouble();
        var first = r["first"].ToDouble();
        var last = r["last"].ToDouble();
        var delta = last >= first ? last - first : last; // counter reset ⇒ read as restarted, never negative
        var value = which switch { "min" => min, "max" => max, "sum" => sum, "delta" => delta, _ => avg };
        return new AggregateBucket(ts, value, min, max, avg, r["count"].ToInt64());
    }

    private static FilterDefinition<SensorReading> TelemetryFilter(string? capabilityId, string? zoneId, DateTime? from, DateTime? to)
    {
        var (lo, hi, _) = Window(from, to, null);
        var b = Builders<SensorReading>.Filter;
        var filter = b.Gte(x => x.Timestamp, lo) & b.Lte(x => x.Timestamp, hi);
        if (!string.IsNullOrEmpty(capabilityId)) filter &= b.Eq(x => x.Meta.CapabilityId, capabilityId);
        if (!string.IsNullOrEmpty(zoneId)) filter &= b.Eq(x => x.Meta.ZoneId, zoneId);
        return filter;
    }

    private static FilterDefinition<DeviceEventLog> EventFilter(string? capabilityId, string? zoneId, DateTime? from, DateTime? to)
    {
        var (lo, hi, _) = Window(from, to, null);
        var b = Builders<DeviceEventLog>.Filter;
        var filter = b.Gte(x => x.Timestamp, lo) & b.Lte(x => x.Timestamp, hi);
        if (!string.IsNullOrEmpty(capabilityId)) filter &= b.Eq(x => x.CapabilityId, capabilityId);
        if (!string.IsNullOrEmpty(zoneId)) filter &= b.Eq(x => x.Meta.ZoneId, zoneId);
        return filter;
    }

    private static async Task<List<ZoneCount>> CountByZone<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, CancellationToken ct)
    {
        var rows = await collection.Aggregate()
            .Match(filter)
            .Group(new BsonDocument { { "_id", "$Meta.ZoneId" }, { "count", new BsonDocument("$sum", 1) } })
            .ToListAsync(ct);
        return rows.Select(r => new ZoneCount(r["_id"].IsBsonNull ? "" : r["_id"].AsString, r["count"].ToInt64())).ToList();
    }

    private static (DateTime from, DateTime to, int limit) Window(DateTime? from, DateTime? to, int? limit)
    {
        var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        return (lo, hi, take);
    }
}
