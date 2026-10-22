// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text;

using Domovoy.DbGateway.Stores;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Read endpoints over the append-only feature store (roadmap P0-5): the domain event-log
/// (<c>/api/events</c>) and numeric telemetry (<c>/api/telemetry</c>). These let you export history for a period
/// and reconstruct who/what changed a device's state — the basis for replay/explainability (Epic 1F) and ML
/// features (Phase 2). The ApiGateway forwards to these via its HistoryController.
///
/// <para><b>Storage-abstraction seam (Epic 3H, Ф1 reference migration):</b> these endpoints are now thin HTTP
/// adapters — parameter parsing, CSV formatting and error mapping only. Every read/aggregation goes through the
/// <see cref="ITelemetryStore"/> domain interface, so no <c>MongoDB.Driver</c> type appears here. Swapping the
/// backend (PostgreSQL, Ф2) is a new <see cref="ITelemetryStore"/> implementation; this file does not change.</para>
/// </summary>
public static class HistoryEndpoints
{
    /// <summary>Body of <c>POST /api/telemetry/aggregate/batch</c> — many series in one round-trip.</summary>
    public record AggregateBatchRequest(
        List<SeriesSpec> Series, DateTime? From, DateTime? To, string? Bucket, string? Agg, int? MaxPoints);

    /// <summary>Body of <c>POST /api/events/latest-by-device</c>.</summary>
    public record LatestByDeviceRequest(List<string> DeviceIds, DateTime? From, DateTime? To);

    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("History").WithOpenApi();

        // GET /api/events?deviceId=&capabilityId=&zoneId=&kind=&from=&to=&limit=
        group.MapGet("/events", async (
            string? deviceId, string? capabilityId, string? zoneId, string? kind,
            DateTime? from, DateTime? to, int? limit, ITelemetryStore store, CancellationToken ct) =>
            Results.Ok(await store.QueryEventsAsync(deviceId, capabilityId, zoneId, kind, from, to, limit, ct)));

        // GET /api/telemetry?deviceId=&capabilityId=&zoneId=&from=&to=&limit=&format=json|csv
        group.MapGet("/telemetry", async (
            string? deviceId, string? capabilityId, string? zoneId,
            DateTime? from, DateTime? to, int? limit, string? format, ITelemetryStore store, CancellationToken ct) =>
        {
            var samples = await store.QueryTelemetryAsync(deviceId, capabilityId, zoneId, from, to, limit, ct);
            // Period export (Epic 1B): CSV for spreadsheets / external tools.
            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                return Results.Text(ToCsv(samples), "text/csv", Encoding.UTF8);
            return Results.Ok(samples);
        });

        // Lightweight sample counters for the ML data-sufficiency check (Epic 2P).
        group.MapGet("/telemetry/count", async (
            string? capabilityId, string? zoneId, DateTime? from, DateTime? to, ITelemetryStore store, CancellationToken ct) =>
            Results.Ok(new { count = await store.CountTelemetryAsync(capabilityId, zoneId, from, to, ct) }));

        group.MapGet("/telemetry/count-by-zone", async (
            string? capabilityId, DateTime? from, DateTime? to, ITelemetryStore store, CancellationToken ct) =>
            Results.Ok(await store.CountTelemetryByZoneAsync(capabilityId, from, to, ct)));

        group.MapGet("/events/count", async (
            string? capabilityId, string? zoneId, DateTime? from, DateTime? to, ITelemetryStore store, CancellationToken ct) =>
            Results.Ok(new { count = await store.CountEventsAsync(capabilityId, zoneId, from, to, ct) }));

        // The timestamp of the oldest recorded event — "how old is the history" for the 3I cold-start gate.
        group.MapGet("/events/earliest", async (ITelemetryStore store, CancellationToken ct) =>
            Results.Ok(new { earliest = await store.EarliestEventAsync(ct) }));

        group.MapGet("/events/count-by-zone", async (
            string? capabilityId, DateTime? from, DateTime? to, ITelemetryStore store, CancellationToken ct) =>
            Results.Ok(await store.CountEventsByZoneAsync(capabilityId, from, to, ct)));

        // GET /api/telemetry/aggregate — minute/hour/day rollups computed on the fly (roadmap Epic 1B).
        group.MapGet("/telemetry/aggregate", async (
            string? deviceId, string? capabilityId, string? zoneId,
            DateTime? from, DateTime? to, string? bucket, string? agg, ITelemetryStore store, CancellationToken ct) =>
        {
            try { return Results.Ok(await store.AggregateTelemetryAsync(deviceId, capabilityId, zoneId, from, to, bucket, agg, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /api/telemetry/aggregate/batch — many (deviceId, capabilityId) series in ONE round-trip.
        group.MapPost("/telemetry/aggregate/batch", async (AggregateBatchRequest req, ITelemetryStore store, CancellationToken ct) =>
        {
            try { return Results.Ok(await store.AggregateBatchAsync(req.Series, req.From, req.To, req.Bucket, req.Agg, req.MaxPoints, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // POST /api/events/latest-by-device — the most recent event-log row per device in ONE round-trip.
        group.MapPost("/events/latest-by-device", async (LatestByDeviceRequest req, ITelemetryStore store, CancellationToken ct) =>
        {
            try { return Results.Ok(await store.LatestByDeviceAsync(req.DeviceIds, req.From, req.To, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }

    private static string ToCsv(IEnumerable<TelemetryRow> samples)
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
}
