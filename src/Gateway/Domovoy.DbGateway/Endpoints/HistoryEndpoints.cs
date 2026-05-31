using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

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
        object? OldValue, object? NewValue, string TriggerSource,
        string? RuleId, string? DecisionId, string? Mode, string? CorrelationId);

    /// <summary>Flattened telemetry sample for the client.</summary>
    public record TelemetryDto(
        DateTime Timestamp, string DeviceId, string ZoneId, string CapabilityId, string? Unit, double Value);

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
                d.OldValue, d.NewValue, d.TriggerSource, d.RuleId, d.DecisionId, d.Mode, d.CorrelationId)));
        });

        // GET /api/telemetry?deviceId=&capabilityId=&zoneId=&from=&to=&limit=
        group.MapGet("/telemetry", async (
            string? deviceId, string? capabilityId, string? zoneId,
            DateTime? from, DateTime? to, int? limit, IMongoDatabase db) =>
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

            return Results.Ok(docs.Select(d => new TelemetryDto(
                d.Timestamp, d.Meta.DeviceId, d.Meta.ZoneId, d.Meta.CapabilityId, d.Meta.Unit, d.Value)));
        });
    }

    private static (DateTime from, DateTime to, int limit) Window(DateTime? from, DateTime? to, int? limit)
    {
        var hi = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var lo = from?.ToUniversalTime() ?? hi - DefaultWindow;
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        return (lo, hi, take);
    }
}
