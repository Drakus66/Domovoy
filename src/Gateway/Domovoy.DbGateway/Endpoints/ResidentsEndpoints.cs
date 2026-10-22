// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD endpoints for tracked residents (roadmap Epic 3D — presence as a platform signal). A resident is a
/// household member whose home/away state the platform tracks; it is optionally linked to a local user (2E)
/// but independent of one (a home can track a guest phone). DbGateway owns the <c>residents</c> collection;
/// the AutomationService reads it to project one virtual <c>person</c> device per resident, and the
/// ApiGateway forwards writes via ResidentsController. Presence itself is live signal state (published on the
/// person device), not stored here — this is only the durable roster.
/// </summary>
public static class ResidentsEndpoints
{
    public const string Collection = "residents";

    /// <summary>Request body for creating/updating a resident (id is route/server assigned).</summary>
    public record ResidentInput(string DisplayName, string? UserId, string? OwnTracksId, bool TrackingEnabled = true);

    public static void MapResidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/residents").WithTags("Residents").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var residents = await Residents(db).Find(FilterDefinition<Resident>.Empty).ToListAsync();
            return Results.Ok(residents);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var resident = await Residents(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return resident is null ? Results.NotFound() : Results.Ok(resident);
        });

        group.MapPost("/", async (ResidentInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                return Results.BadRequest(new { error = "resident display name is required" });

            var now = DateTime.UtcNow;
            var resident = new Resident
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = input.DisplayName.Trim(),
                UserId = Clean(input.UserId),
                OwnTracksId = Clean(input.OwnTracksId),
                TrackingEnabled = input.TrackingEnabled,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await Residents(db).InsertOneAsync(resident);
            return Results.Created($"/api/residents/{resident.Id}", resident);
        });

        group.MapPut("/{id}", async (string id, ResidentInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                return Results.BadRequest(new { error = "resident display name is required" });

            var update = Builders<Resident>.Update
                .Set(x => x.DisplayName, input.DisplayName.Trim())
                .Set(x => x.UserId, Clean(input.UserId))
                .Set(x => x.OwnTracksId, Clean(input.OwnTracksId))
                .Set(x => x.TrackingEnabled, input.TrackingEnabled)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Residents(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Residents(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IMongoCollection<Resident> Residents(IMongoDatabase db) =>
        db.GetCollection<Resident>(Collection);
}
