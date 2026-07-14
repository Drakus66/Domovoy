// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for custom dashboard tabs plus the hidden-auto-spheres preference singleton
/// (custom dashboards epic). The WebUI talks to these via the ApiGateway DashboardsController.
/// Documents are free-form presentation state — validation only guards structural invariants
/// (a name, known item types, ids where the type needs them), never device existence: a dashboard
/// referencing a removed device renders a placeholder client-side instead of failing here.
/// </summary>
public static class DashboardEndpoints
{
    public const string Collection = "dashboards";
    public const string PrefsCollection = "dashboard_prefs";

    /// <summary>Item types the WebUI knows how to render.</summary>
    public static readonly HashSet<string> ItemTypes = new(StringComparer.Ordinal)
    {
        "device", "capability", "chart", "modes",
    };

    /// <summary>Sphere category keys as produced by the WebUI's archetype→category mapping.</summary>
    public static readonly HashSet<string> SphereCategories = new(StringComparer.Ordinal)
    {
        "light", "switch", "climate", "sensor", "security", "energy", "other",
    };

    /// <summary>Request body for creating/updating a dashboard (id/order are server assigned).</summary>
    public record DashboardInput(string Name, string? Icon, List<DashboardSection>? Sections);

    public record OrderUpdate(List<string> Ids);

    public record PrefsUpdate(List<string>? HiddenSpheres);

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards")
            .WithTags("Dashboards")
            .WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var dashboards = await Dashboards(db)
                .Find(FilterDefinition<DashboardDocument>.Empty)
                .SortBy(x => x.Order).ThenBy(x => x.Name)
                .ToListAsync();
            return Results.Ok(dashboards);
        });

        // Literal segments must not be swallowed by /{id}; ASP.NET routes literals first, so
        // /prefs and /order are safe alongside the template.
        group.MapGet("/prefs", async (IMongoDatabase db) => Results.Ok(await GetPrefsOrDefault(db)));

        group.MapPut("/prefs", async (PrefsUpdate body, IMongoDatabase db) =>
        {
            var prefs = new DashboardPrefs
            {
                HiddenSpheres = (body.HiddenSpheres ?? new List<string>())
                    .Where(SphereCategories.Contains).Distinct(StringComparer.Ordinal).ToList(),
                UpdatedAt = DateTime.UtcNow,
            };
            await Prefs(db).ReplaceOneAsync(
                x => x.Id == DashboardPrefs.SingletonId, prefs, new ReplaceOptions { IsUpsert = true });
            return Results.Ok(prefs);
        });

        group.MapPut("/order", async (OrderUpdate body, IMongoDatabase db) =>
        {
            if (body.Ids is null || body.Ids.Count == 0)
                return Results.BadRequest(new { error = "ids are required" });

            var writes = body.Ids.Select((id, index) => new UpdateOneModel<DashboardDocument>(
                Builders<DashboardDocument>.Filter.Eq(x => x.Id, id),
                Builders<DashboardDocument>.Update.Set(x => x.Order, index).Set(x => x.UpdatedAt, DateTime.UtcNow)))
                .ToList<WriteModel<DashboardDocument>>();
            await Dashboards(db).BulkWriteAsync(writes);
            return Results.NoContent();
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var dashboard = await Dashboards(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
        });

        group.MapPost("/", async (DashboardInput input, IMongoDatabase db) =>
        {
            var (doc, error) = Normalize(input);
            if (doc is null)
                return Results.BadRequest(new { error });

            doc.Id = Guid.NewGuid().ToString();
            var maxOrder = await Dashboards(db)
                .Find(FilterDefinition<DashboardDocument>.Empty)
                .SortByDescending(x => x.Order)
                .Project(x => x.Order)
                .FirstOrDefaultAsync();
            doc.Order = maxOrder + 1;

            await Dashboards(db).InsertOneAsync(doc);
            return Results.Created($"/api/dashboards/{doc.Id}", doc);
        });

        group.MapPut("/{id}", async (string id, DashboardInput input, IMongoDatabase db) =>
        {
            var (doc, error) = Normalize(input);
            if (doc is null)
                return Results.BadRequest(new { error });

            var update = Builders<DashboardDocument>.Update
                .Set(x => x.Name, doc.Name)
                .Set(x => x.Icon, doc.Icon)
                .Set(x => x.Sections, doc.Sections)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Dashboards(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Dashboards(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>
    /// Validate and normalize an input into a fresh document (id/order/timestamps set by caller
    /// or preserved on update). Returns (null, error) on invalid input. Pure — unit-testable without Mongo.
    /// </summary>
    public static (DashboardDocument? Doc, string? Error) Normalize(DashboardInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return (null, "dashboard name is required");

        var sections = input.Sections ?? new List<DashboardSection>();
        foreach (var section in sections)
        {
            section.Title = section.Title?.Trim() ?? string.Empty;
            section.Items ??= new List<DashboardItem>();
            foreach (var item in section.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Type) || !ItemTypes.Contains(item.Type))
                    return (null, $"unknown item type '{item.Type}'");
                if (item.Type is "device" or "capability" or "chart" && string.IsNullOrWhiteSpace(item.DeviceId))
                    return (null, $"item of type '{item.Type}' requires a deviceId");
                if (item.Type is "capability" or "chart" && string.IsNullOrWhiteSpace(item.CapabilityId))
                    return (null, $"item of type '{item.Type}' requires a capabilityId");
            }
        }

        var now = DateTime.UtcNow;
        return (new DashboardDocument
        {
            Name = input.Name.Trim(),
            Icon = string.IsNullOrWhiteSpace(input.Icon) ? null : input.Icon.Trim(),
            Sections = sections,
            CreatedAt = now,
            UpdatedAt = now,
        }, null);
    }

    private static async Task<DashboardPrefs> GetPrefsOrDefault(IMongoDatabase db)
    {
        var prefs = await Prefs(db).Find(x => x.Id == DashboardPrefs.SingletonId).FirstOrDefaultAsync();
        return prefs ?? new DashboardPrefs();
    }

    private static IMongoCollection<DashboardDocument> Dashboards(IMongoDatabase db) =>
        db.GetCollection<DashboardDocument>(Collection);

    private static IMongoCollection<DashboardPrefs> Prefs(IMongoDatabase db) =>
        db.GetCollection<DashboardPrefs>(PrefsCollection);
}
