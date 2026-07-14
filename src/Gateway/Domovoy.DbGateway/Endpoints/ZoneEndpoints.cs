// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD endpoints for areas/zones (roadmap P0-3). The WebUI manages zones through these (the
/// ApiGateway forwards via its ZonesController). Devices are bound to a zone through the
/// <c>/api/capability-devices/{id}/zone</c> endpoint, not here.
/// </summary>
public static class ZoneEndpoints
{
    public const string Collection = "zones";

    /// <summary>Request body for creating/updating a zone (id is route/server assigned).</summary>
    public record ZoneInput(string Name, string? Description, string? ParentZoneId, string? Kind, int Order = 0);

    public static void MapZoneEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/zones")
            .WithTags("Zones")
            .WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var zones = await Zones(db).Find(FilterDefinition<Zone>.Empty).ToListAsync();
            return Results.Ok(zones);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var zone = await Zones(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return zone is null ? Results.NotFound() : Results.Ok(zone);
        });

        group.MapPost("/", async (ZoneInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return Results.BadRequest(new { error = "zone name is required" });

            var now = DateTime.UtcNow;
            var zone = new Zone
            {
                Id = Guid.NewGuid().ToString(),
                Name = input.Name.Trim(),
                Description = input.Description,
                ParentZoneId = string.IsNullOrWhiteSpace(input.ParentZoneId) ? null : input.ParentZoneId,
                Kind = input.Kind,
                Order = input.Order,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await Zones(db).InsertOneAsync(zone);
            return Results.Created($"/api/zones/{zone.Id}", zone);
        });

        group.MapPut("/{id}", async (string id, ZoneInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return Results.BadRequest(new { error = "zone name is required" });
            if (input.ParentZoneId == id)
                return Results.BadRequest(new { error = "a zone cannot be its own parent" });

            var update = Builders<Zone>.Update
                .Set(x => x.Name, input.Name.Trim())
                .Set(x => x.Description, input.Description)
                .Set(x => x.ParentZoneId, string.IsNullOrWhiteSpace(input.ParentZoneId) ? null : input.ParentZoneId)
                .Set(x => x.Kind, input.Kind)
                .Set(x => x.Order, input.Order)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Zones(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var zone = await Zones(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (zone is null) return Results.NotFound();

            // Reparent direct children onto this zone's parent so the graph stays connected,
            // and unassign any devices that pointed at the deleted zone.
            await Zones(db).UpdateManyAsync(
                x => x.ParentZoneId == id,
                Builders<Zone>.Update.Set(x => x.ParentZoneId, zone.ParentZoneId).Set(x => x.UpdatedAt, DateTime.UtcNow));

            var devices = db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);
            await devices.UpdateManyAsync(
                x => x.ZoneId == id,
                Builders<CapabilityDeviceDocument>.Update.Set(x => x.ZoneId, string.Empty).Set(x => x.LastUpdated, DateTime.UtcNow));

            await Zones(db).DeleteOneAsync(x => x.Id == id);
            return Results.NoContent();
        });
    }

    private static IMongoCollection<Zone> Zones(IMongoDatabase db) =>
        db.GetCollection<Zone>(Collection);
}
