// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Read endpoints for the capability device read-model (roadmap Step 4). The WebUI lists/inspects
/// devices through these (the ApiGateway forwards reads here via its CapabilityDevicesController).
/// Discovery writes happen in the EventInterceptor; the only mutation here besides the manual
/// zone/archetype assignments is pruning offline devices from the registry.
/// </summary>
public static class CapabilityDeviceEndpoints
{
    public const string Collection = "capability_devices";

    /// <summary>Request body for binding a device to a zone (empty/null zoneId unassigns).</summary>
    public record ZoneAssignment(string? ZoneId);

    /// <summary>Outcome of <see cref="DeleteOfflineAsync"/> — maps to 204 / 404 / 409.</summary>
    public enum DeleteOutcome { Deleted, NotFound, StillOnline }

    /// <summary>Request body for the manual archetype override (empty/null reverts to the auto value).</summary>
    public record ArchetypeAssignment(string? Archetype);

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

        // Remove a device from the registry. Only offline devices may be deleted (409 otherwise);
        // if the device comes back it re-announces and the discovery upsert recreates the document,
        // so this is how stale/decommissioned devices are pruned without losing re-integration.
        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
            await DeleteOfflineAsync(db, id) switch
            {
                DeleteOutcome.Deleted => Results.NoContent(),
                DeleteOutcome.StillOnline => Results.Conflict(
                    new { error = "device is online; only offline devices can be deleted" }),
                _ => Results.NotFound(),
            });

        // Manual archetype override (Epic 2D). Empty/null reverts to the auto-classified value. The
        // discovery path only writes AutoArchetype, so this override survives re-announces.
        group.MapPut("/{id}/archetype", async (string id, ArchetypeAssignment body, IMongoDatabase db) =>
        {
            var archetype = string.IsNullOrWhiteSpace(body.Archetype) ? null : body.Archetype.Trim().ToLowerInvariant();
            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.Archetype, archetype)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>
    /// Deletes a device document only while it is offline — the filter carries the IsOnline guard so
    /// the check-and-delete is a single atomic operation (no race with a concurrent state report).
    /// </summary>
    public static async Task<DeleteOutcome> DeleteOfflineAsync(IMongoDatabase db, string id)
    {
        var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
        var result = await collection.DeleteOneAsync(x => x.Id == id && !x.IsOnline);
        if (result.DeletedCount > 0)
            return DeleteOutcome.Deleted;
        var exists = await collection.Find(x => x.Id == id).AnyAsync();
        return exists ? DeleteOutcome.StillOnline : DeleteOutcome.NotFound;
    }
}
