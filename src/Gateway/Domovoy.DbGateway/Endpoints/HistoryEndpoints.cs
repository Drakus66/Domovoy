// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text;

using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using MongoDB.Bson;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Read endpoints over the append-only feature store (roadmap P0-5): the domain event-log
/// (<c>/api/events</c>) and numeric telemetry (<c>/api/telemetry</c>). These let you export history
/// for a period and reconstruct who/what changed a device's state — the basis for replay/explainability
/// (Epic 1F) and ML features (Phase 2). The ApiGateway forwards to these via its HistoryController.
/// </summary>
public static class HistoryEndpoints
{
    private const int DefaultLimit = 500;
    private const int MaxLimit = 5000;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    /// <summary>Flattened event-log record for the client (Meta unpacked, no ObjectId).</summary>
    public record EventLogDto(
        DateTime Timestamp, string DeviceId, string ZoneId, string Kind, string CapabilityId,
        object? OldValue, object? NewValue, string TriggerSource, string? TriggerId,
        string? RuleId, string? DecisionId, string? Mode, string? CorrelationId);

    /// <summary>Flattened telemetry sample for the client.</summary>
    public record TelemetryDto(
        DateTime Timestamp, string DeviceId, string ZoneId, string CapabilityId, string? Unit, double Value);

    /// <summary>One time bucket of aggregated telemetry (roadmap Epic 1B). <c>Value</c> is the requested agg.</summary>
    public record AggregateBucket(DateTime Timestamp, double Value, double Min, double Max, double Avg, long Count);

    /// <summary>One (device, capability) series requested in a batch aggregation.</summary>
    public record SeriesSpec(string DeviceId, string CapabilityId);

    /// <summary>Aggregated buckets for one requested series (dashboard sparklines / composed charts).</summary>
    public record SeriesResult(string DeviceId, string CapabilityId, IReadOnlyList<AggregateBucket> Buckets);

    /// <summary>Body of <c>POST /api/telemetry/aggregate/batch</c> — many series in one round-trip.</summary>
    public record AggregateBatchRequest(
        List<SeriesSpec> Series, DateTime? From, DateTime? To, string? Bucket, string? Agg, int? MaxPoints);

    /// <summary>The latest event-log row for one device (batch provenance — "who changed it last").</summary>
    public record LatestEventDto(
        string DeviceId, DateTime Timestamp, string CapabilityId,
        string TriggerSource, string? TriggerId, string? RuleId, string? CorrelationId, object? NewValue);

    /// <summary>Body of <c>POST /api/events/latest-by-device</c>.</summary>
    public record LatestByDeviceRequest(List<string> DeviceIds, DateTime? From, DateTime? To);

    /// <summary>Cap on series/devices per batch request — bounds the fan-out of a single call.</summary>
    private const int MaxBatchItems = 200;

    /// <summary>Default points per sparkline series when the caller does not specify.</summary>
    private const int DefaultMaxPoints = 48;

    /// <summary>Bucket size → MongoDB <c>$dateTrunc</c> unit. Closed allow-list (no injection).</summary>
    private static readonly Dictionary<string, string> Buckets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minute"] = "minute", ["hour"] = "hour", ["day"] = "day",
    };

    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("History").WithOpenApi();

        // GET /api/events?deviceId=&capabilityId=&zoneId=&kind=&from=&to=&limit=
        group.MapGet("/events", async (
            string? deviceId, string? capabilityId, string? zoneId, string? kind,
            DateTime? from, DateTime? to, int? limit, IMongoDatabase db) =>
        {
            var (lo, hi, take) = Window(from, to, limit);
            var b = Builders<DeviceEventLog>.Filter;
            var filters = new List<FilterDefinition<DeviceEventLog>>
            {
                b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi),
            };
            if (!string.IsNullOrEmpty(deviceId)) filters.Add(b.Eq(x => x.Meta.DeviceId, deviceId));
            if (!string.IsNullOrEmpty(zoneId)) filters.Add(b.Eq(x => x.Meta.ZoneId, zoneId));
            if (!string.IsNullOrEmpty(kind)) filters.Add(b.Eq(x => x.Meta.Kind, kind));
            if (!string.IsNullOrEmpty(capabilityId)) filters.Add(b.Eq(x => x.CapabilityId, capabilityId));

            var docs = await db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection)
                .Find(b.And(filters))
                .SortByDescending(x => x.Timestamp)
                .Limit(take)
                .ToListAsync();

            return Results.Ok(docs.Select(d => new EventLogDto(
                d.Timestamp, d.Meta.DeviceId, d.Meta.ZoneId, d.Meta.Kind, d.CapabilityId,
                d.OldValue, d.NewValue, d.TriggerSource, d.TriggerId, d.RuleId, d.DecisionId, d.Mode, d.CorrelationId)));
        });

        // GET /api/telemetry?deviceId=&capabilityId=&zoneId=&from=&to=&limit=&format=json|csv
        group.MapGet("/telemetry", async (
            string? deviceId, string? capabilityId, string? zoneId,
            DateTime? from, DateTime? to, int? limit, string? format, IMongoDatabase db) =>
        {
            var (lo, hi, take) = Window(from, to, limit);
            var b = Builders<SensorReading>.Filter;
            var filters = new List<FilterDefinition<SensorReading>>
            {
                b.Gte(x => x.Timestamp, lo), b.Lte(x => x.Timestamp, hi),
            };
            if (!string.IsNullOrEmpty(deviceId)) filters.Add(b.Eq(x => x.Meta.DeviceId, deviceId));
            if (!string.IsNullOrEmpty(zoneId)) filters.Add(b.Eq(x => x.Meta.ZoneId, zoneId));
            if (!string.IsNullOrEmpty(capabilityId)) filters.Add(b.Eq(x => x.Meta.CapabilityId, capabilityId));

            var docs = await db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection)
                .Find(b.And(filters))
                .SortByDescending(x => x.Timestamp)
                .Limit(take)
                .ToListAsync();

            var samples = docs.Select(d => new TelemetryDto(
                d.Timestamp, d.Meta.DeviceId, d.Meta.ZoneId, d.Meta.CapabilityId, d.Meta.Unit, d.Value)).ToList();

            // Period export (Epic 1B): CSV for spreadsheets / external tools.
            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                return Results.Text(ToCsv(samples), "text/csv", Encoding.UTF8);

            return Results.Ok(samples);
        });

        // Lightweight sample counters for the ML data-sufficiency check (Epic 2P) — "is there enough history
        // to train?" answered by Mongo counts/aggregation instead of paging the raw series to the caller.

        // GET /api/telemetry/count?capabilityId=&zoneId=&from=&to=
        group.MapGet("/telemetry/count", async (
            string? capabilityId, string? zoneId, DateTime? from, DateTime? to, IMongoDatabase db) =>
        {
            var count = await db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection)
                .CountDocumentsAsync(TelemetryFilter(capabilityId, zoneId, from, to));
            return Results.Ok(new { count });
        });

        // GET /api/telemetry/count-by-zone?capabilityId=&from=&to= → [{ zoneId, count }]
        group.MapGet("/telemetry/count-by-zone", async (
            string? capabilityId, DateTime? from, DateTime? to, IMongoDatabase db) =>
        {
            var rows = await CountByZone(
                db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection),
                TelemetryFilter(capabilityId, null, from, to));
            return Results.Ok(rows);
        });

        // GET /api/events/count?capabilityId=&zoneId=&from=&to=
        group.MapGet("/events/count", async (
            string? capabilityId, string? zoneId, DateTime? from, DateTime? to, IMongoDatabase db) =>
        {
            var count = await db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection)
                .CountDocumentsAsync(EventFilter(capabilityId, zoneId, from, to));
            return Results.Ok(new { count });
        });

        // GET /api/events/count-by-zone?capabilityId=&from=&to= → [{ zoneId, count }]
        group.MapGet("/events/count-by-zone", async (
            string? capabilityId, DateTime? from, DateTime? to, IMongoDatabase db) =>
        {
            var rows = await CountByZone(
                db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection),
                EventFilter(capabilityId, null, from, to));
            return Results.Ok(rows);
        });

        // GET /api/telemetry/aggregate?deviceId=&capabilityId=&zoneId=&from=&to=&bucket=hour&agg=avg
        // Minute/hour/day rollups computed on the fly (roadmap Epic 1B) — e.g. "zone temperature per hour".
        group.MapGet("/telemetry/aggregate", async (
            string? deviceId, string? capabilityId, string? zoneId,
            DateTime? from, DateTime? to, string? bucket, string? agg, IMongoDatabase db) =>
        {
            if (!Buckets.TryGetValue(bucket ?? "hour", out var unit))
                return Results.BadRequest(new { error = "bucket must be minute, hour or day" });

            var which = (agg ?? "avg").ToLowerInvariant();
            if (which is not ("avg" or "min" or "max"))
                return Results.BadRequest(new { error = "agg must be avg, min or max" });

            var (lo, hi, _) = Window(from, to, null);

            var match = new BsonDocument
            {
                { "Timestamp", new BsonDocument { { "$gte", lo }, { "$lte", hi } } },
            };
            if (!string.IsNullOrEmpty(deviceId)) match["Meta.DeviceId"] = deviceId;
            if (!string.IsNullOrEmpty(zoneId)) match["Meta.ZoneId"] = zoneId;
            if (!string.IsNullOrEmpty(capabilityId)) match["Meta.CapabilityId"] = capabilityId;

            var pipeline = new[]
            {
                new BsonDocument("$match", match),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument("$dateTrunc", new BsonDocument
                        { { "date", "$Timestamp" }, { "unit", unit }, { "binSize", 1 } }) },
                    { "avg", new BsonDocument("$avg", "$Value") },
                    { "min", new BsonDocument("$min", "$Value") },
                    { "max", new BsonDocument("$max", "$Value") },
                    { "count", new BsonDocument("$sum", 1) },
                }),
                new BsonDocument("$sort", new BsonDocument("_id", 1)),
                new BsonDocument("$limit", MaxLimit),
            };

            var rows = await db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection)
                .Aggregate<BsonDocument>(pipeline)
                .ToListAsync();

            var buckets = rows.Select(r =>
            {
                var avg = r["avg"].ToDouble();
                var min = r["min"].ToDouble();
                var max = r["max"].ToDouble();
                var value = which switch { "min" => min, "max" => max, _ => avg };
                return new AggregateBucket(r["_id"].ToUniversalTime(), value, min, max, avg, r["count"].ToInt64());
            });

            return Results.Ok(buckets);
        });

        // POST /api/telemetry/aggregate/batch — many (deviceId, capabilityId) series in ONE round-trip.
        // The dashboard fill (sparklines per tile) and composed multi-series charts would otherwise fire
        // one aggregate request per series; this coalesces them into a single Mongo aggregation.
        group.MapPost("/telemetry/aggregate/batch", async (AggregateBatchRequest req, IMongoDatabase db) =>
        {
            try
            {
                var results = await AggregateBatchAsync(
                    db, req.Series, req.From, req.To, req.Bucket, req.Agg, req.MaxPoints);
                return Results.Ok(results);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // POST /api/events/latest-by-device — the most recent event-log row per device in ONE round-trip.
        // Feeds the per-tile "last changed by …" provenance chip without an N+1 of /api/events calls.
        group.MapPost("/events/latest-by-device", async (LatestByDeviceRequest req, IMongoDatabase db) =>
        {
            try
            {
                var rows = await LatestByDeviceAsync(db, req.DeviceIds, req.From, req.To);
                return Results.Ok(rows);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    /// <summary>
    /// Aggregate telemetry for many (device, capability) series at once (roadmap dashboard fill). One Mongo
    /// aggregation with an <c>$or</c> over the requested pairs and a compound group key (device, capability,
    /// truncated time); results are split back per series and capped to the most-recent <paramref name="maxPoints"/>.
    /// Extracted from the endpoint so it can be integration-tested directly against the store.
    /// </summary>
    public static async Task<List<SeriesResult>> AggregateBatchAsync(
        IMongoDatabase db, IReadOnlyList<SeriesSpec>? series,
        DateTime? from, DateTime? to, string? bucket, string? agg, int? maxPoints)
    {
        if (!Buckets.TryGetValue(bucket ?? "hour", out var unit))
            throw new ArgumentException("bucket must be minute, hour or day");

        var which = (agg ?? "avg").ToLowerInvariant();
        if (which is not ("avg" or "min" or "max"))
            throw new ArgumentException("agg must be avg, min or max");

        // De-duplicate and drop blanks so a repeated series doesn't skew the $or or the response.
        var specs = (series ?? Array.Empty<SeriesSpec>())
            .Where(s => !string.IsNullOrEmpty(s.DeviceId) && !string.IsNullOrEmpty(s.CapabilityId))
            .Distinct()
            .ToList();
        if (specs.Count > MaxBatchItems)
            throw new ArgumentException($"at most {MaxBatchItems} series per request");
        if (specs.Count == 0) return new List<SeriesResult>();

        var cap = Math.Clamp(maxPoints ?? DefaultMaxPoints, 1, MaxLimit);
        var (lo, hi, _) = Window(from, to, null);

        var orConds = new BsonArray(specs.Select(s => new BsonDocument
        {
            { "Meta.DeviceId", s.DeviceId }, { "Meta.CapabilityId", s.CapabilityId },
        }));

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "Timestamp", new BsonDocument { { "$gte", lo }, { "$lte", hi } } },
                { "$or", orConds },
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "d", "$Meta.DeviceId" },
                        { "c", "$Meta.CapabilityId" },
                        { "t", new BsonDocument("$dateTrunc", new BsonDocument
                            { { "date", "$Timestamp" }, { "unit", unit }, { "binSize", 1 } }) },
                    } },
                { "avg", new BsonDocument("$avg", "$Value") },
                { "min", new BsonDocument("$min", "$Value") },
                { "max", new BsonDocument("$max", "$Value") },
                { "count", new BsonDocument("$sum", 1) },
            }),
            new BsonDocument("$sort", new BsonDocument { { "_id.d", 1 }, { "_id.c", 1 }, { "_id.t", 1 } }),
            new BsonDocument("$limit", MaxLimit),
        };

        var rows = await db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection)
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync();

        // Split the flat, time-sorted rows back into per-series bucket lists.
        var byKey = new Dictionary<(string, string), List<AggregateBucket>>();
        foreach (var r in rows)
        {
            var id = r["_id"].AsBsonDocument;
            var key = (id["d"].AsString, id["c"].AsString);
            var avg = r["avg"].ToDouble();
            var min = r["min"].ToDouble();
            var max = r["max"].ToDouble();
            var value = which switch { "min" => min, "max" => max, _ => avg };
            if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<AggregateBucket>();
            list.Add(new AggregateBucket(id["t"].ToUniversalTime(), value, min, max, avg, r["count"].ToInt64()));
        }

        // Return in the requested order; keep only the most-recent `cap` buckets of each series.
        return specs.Select(s =>
        {
            var list = byKey.TryGetValue((s.DeviceId, s.CapabilityId), out var l)
                ? (l.Count > cap ? l.GetRange(l.Count - cap, cap) : l)
                : new List<AggregateBucket>();
            return new SeriesResult(s.DeviceId, s.CapabilityId, list);
        }).ToList();
    }

    /// <summary>
    /// The most recent event-log row for each requested device in one aggregation (batch provenance).
    /// Matches the device set within the window, sorts newest-first and takes <c>$first</c> per device.
    /// Devices with no events in the window are simply absent from the result.
    /// </summary>
    public static async Task<List<LatestEventDto>> LatestByDeviceAsync(
        IMongoDatabase db, IReadOnlyList<string>? deviceIds, DateTime? from, DateTime? to)
    {
        var ids = (deviceIds ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (ids.Count > MaxBatchItems)
            throw new ArgumentException($"at most {MaxBatchItems} devices per request");
        if (ids.Count == 0) return new List<LatestEventDto>();

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

        var rows = await db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection)
            .Aggregate<BsonDocument>(pipeline)
            .ToListAsync();

        string? Str(BsonDocument d, string f) => d.TryGetValue(f, out var v) && !v.IsBsonNull ? v.AsString : null;

        return rows.Select(r => new LatestEventDto(
            r["_id"].AsString,
            r["Timestamp"].ToUniversalTime(),
            Str(r, "CapabilityId") ?? string.Empty,
            Str(r, "TriggerSource") ?? string.Empty,
            Str(r, "TriggerId"),
            Str(r, "RuleId"),
            Str(r, "CorrelationId"),
            r.TryGetValue("NewValue", out var nv) && !nv.IsBsonNull ? BsonTypeMapper.MapToDotNetValue(nv) : null))
            .ToList();
    }

    private static string ToCsv(IEnumerable<TelemetryDto> samples)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp,deviceId,zoneId,capabilityId,unit,value");
        foreach (var s in samples)
            sb.Append(s.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.DeviceId).Append(',').Append(s.ZoneId).Append(',')
              .Append(s.CapabilityId).Append(',').Append(s.Unit).Append(',')
              .Append(s.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }

    private static FilterDefinition<SensorReading> TelemetryFilter(
        string? capabilityId, string? zoneId, DateTime? from, DateTime? to)
    {
        var (lo, hi, _) = Window(from, to, null);
        var b = Builders<SensorReading>.Filter;
        var filter = b.Gte(x => x.Timestamp, lo) & b.Lte(x => x.Timestamp, hi);
        if (!string.IsNullOrEmpty(capabilityId)) filter &= b.Eq(x => x.Meta.CapabilityId, capabilityId);
        if (!string.IsNullOrEmpty(zoneId)) filter &= b.Eq(x => x.Meta.ZoneId, zoneId);
        return filter;
    }

    private static FilterDefinition<DeviceEventLog> EventFilter(
        string? capabilityId, string? zoneId, DateTime? from, DateTime? to)
    {
        var (lo, hi, _) = Window(from, to, null);
        var b = Builders<DeviceEventLog>.Filter;
        var filter = b.Gte(x => x.Timestamp, lo) & b.Lte(x => x.Timestamp, hi);
        if (!string.IsNullOrEmpty(capabilityId)) filter &= b.Eq(x => x.CapabilityId, capabilityId);
        if (!string.IsNullOrEmpty(zoneId)) filter &= b.Eq(x => x.Meta.ZoneId, zoneId);
        return filter;
    }

    /// <summary>Group matching documents by <c>Meta.ZoneId</c> and count each zone (one aggregation round-trip).</summary>
    private static async Task<List<ZoneCount>> CountByZone<T>(
        IMongoCollection<T> collection, FilterDefinition<T> filter)
    {
        var rows = await collection.Aggregate()
            .Match(filter)
            .Group(new BsonDocument
            {
                { "_id", "$Meta.ZoneId" },
                { "count", new BsonDocument("$sum", 1) },
            })
            .ToListAsync();

        return rows
            .Select(r => new ZoneCount(r["_id"].IsBsonNull ? "" : r["_id"].AsString, r["count"].ToInt64()))
            .ToList();
    }

    /// <summary>Per-zone sample count for the ML data-sufficiency check (Epic 2P).</summary>
    public record ZoneCount(string ZoneId, long Count);

    private static (DateTime from, DateTime to, int limit) Window(DateTime? from, DateTime? to, int? limit)
    {
        var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        return (lo, hi, take);
    }
}
