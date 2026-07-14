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
