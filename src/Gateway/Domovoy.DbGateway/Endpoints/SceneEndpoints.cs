// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Scenes;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for first-class scenes (roadmap Epic 3B) in the <c>scenes</c> collection. A scene is a named
/// snapshot of target capability states; the WebUI manages it via the ApiGateway proxy, the ApiGateway
/// activates it (fan-out of <c>DeviceCommandV1</c>), and the AutomationService loads scenes over HTTP so
/// the <c>scene</c> rule action can resolve them. Storing (create/update) never touches the house — only
/// activation does — so this file is pure persistence, mirroring <see cref="AutomationEndpoints"/>.
/// </summary>
public static class SceneEndpoints
{
    public const string Collection = "scenes";

    public static void MapSceneEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scenes").WithTags("Scenes").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var scenes = await Scenes(db).Find(FilterDefinition<Scene>.Empty).ToListAsync();
            return Results.Ok(scenes);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var scene = await Scenes(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return scene is null ? Results.NotFound() : Results.Ok(scene);
        });

        group.MapPost("/", async (Scene scene, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(scene.Name))
                return Results.BadRequest(new { error = "scene name is required" });

            NormalizeJsonValues(scene);
            scene.Id = Guid.NewGuid().ToString();
            scene.CreatedAt = scene.UpdatedAt = DateTime.UtcNow;
            await Scenes(db).InsertOneAsync(scene);
            return Results.Created($"/api/scenes/{scene.Id}", scene);
        });

        group.MapPut("/{id}", async (string id, Scene scene, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(scene.Name))
                return Results.BadRequest(new { error = "scene name is required" });

            var existing = await Scenes(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (existing is null) return Results.NotFound();

            NormalizeJsonValues(scene);
            scene.Id = id;
            scene.CreatedAt = existing.CreatedAt;
            scene.UpdatedAt = DateTime.UtcNow;
            await Scenes(db).ReplaceOneAsync(x => x.Id == id, scene);
            return Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Scenes(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>
    /// HTTP binding leaves the <c>object</c>-typed target values as <see cref="JsonElement"/>, which the
    /// Mongo <c>ObjectSerializer</c> (deliberately) refuses to persist. Flatten them to BCL primitives at
    /// the boundary so scenes store as plain BSON regardless of who posted them (WebUI Re-Capture, manual
    /// edit, assistant). Public so tests can exercise the exact endpoint logic. Mirrors
    /// <see cref="AutomationEndpoints.NormalizeJsonValues"/>.
    /// </summary>
    public static void NormalizeJsonValues(Scene scene)
    {
        foreach (var target in scene.Targets)
        {
            if (target.Set is null) continue;
            foreach (var key in target.Set.Keys.ToList()) target.Set[key] = ToPlain(target.Set[key]);
        }
    }

    private static object? ToPlain(object? value) => value switch
    {
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Array => e.EnumerateArray().Select(x => ToPlain(x)).ToList(),
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToPlain(p.Value)),
            _ => null,
        },
        _ => value,
    };

    private static IMongoCollection<Scene> Scenes(IMongoDatabase db) =>
        db.GetCollection<Scene>(Collection);
}
