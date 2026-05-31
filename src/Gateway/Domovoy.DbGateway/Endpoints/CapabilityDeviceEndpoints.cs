using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Read endpoints for the capability device read-model (roadmap Step 4). The WebUI lists/inspects
/// devices through these (the ApiGateway forwards reads here via its CapabilityDevicesController).
/// Writes happen in the EventInterceptor.
/// </summary>
public static class CapabilityDeviceEndpoints
{
    public const string Collection = "capability_devices";

    /// <summary>Request body for binding a device to a zone (empty/null zoneId unassigns).</summary>
    public record ZoneAssignment(string? ZoneId);

    public static void MapCapabilityDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/capability-devices")
            .WithTags("CapabilityDevices")
            .WithOpenApi();

        // List, optionally filtered by zone (?zoneId=...; empty string returns unassigned devices).
        group.MapGet("/", async (string? zoneId, IMongoDatabase db) =>
        {
            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var filter = zoneId is null
                ? FilterDefinition<CapabilityDeviceDocument>.Empty
                : Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.ZoneId, zoneId);
            var all = await collection.Find(filter).ToListAsync();
            return Results.Ok(all);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var doc = await collection.Find(x => x.Id == id).FirstOrDefaultAsync();
            return doc is null ? Results.NotFound() : Results.Ok(doc);
        });

        // Bind a device to a zone (or unassign with empty/null zoneId). Manual assignment that the
        // discovery path must not clobber — see EventInterceptor (ZoneId is SetOnInsert only).
        group.MapPut("/{id}/zone", async (string id, ZoneAssignment body, IMongoDatabase db) =>
        {
            var zoneId = string.IsNullOrWhiteSpace(body.ZoneId) ? string.Empty : body.ZoneId.Trim();

            if (zoneId.Length > 0)
            {
                var zoneExists = await db.GetCollection<Zone>(ZoneEndpoints.Collection)
                    .Find(z => z.Id == zoneId).AnyAsync();
                if (!zoneExists)
                    return Results.BadRequest(new { error = $"zone '{zoneId}' does not exist" });
            }

            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.ZoneId, zoneId)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
