// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Ml;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// ML training tasks (Epic 2P) in the <c>ml_tasks</c> collection — the runtime-editable "what/from what/within
/// which limits to learn" that replaces the env-only <c>Automation.TrainCapability</c>. CRUD is driven by the
/// WebUI ML hub; the AutomationService trainer lists enabled tasks each cycle and reports the outcome of every
/// attempt through the dedicated status endpoint (a <c>$set</c> of <c>Status</c> only, so trainer writes never
/// race user edits). Tasks join to registered models by target capability (unique per task), so pre-existing
/// <c>ml_models</c> documents need no migration.
/// </summary>
public static class MlTaskEndpoints
{
    public const string Collection = "ml_tasks";

    private static bool _indexEnsured;

    public static void MapMlTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ml/tasks").WithTags("ML tasks").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var docs = await Tasks(db).Find(FilterDefinition<MlTaskDocument>.Empty)
                .SortBy(x => x.CreatedAt).ToListAsync();
            return Results.Ok(docs.Select(ToTask));
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var doc = await Tasks(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return doc is null ? Results.NotFound() : Results.Ok(ToTask(doc));
        });

        group.MapPost("/", async (MlTask body, IMongoDatabase db) =>
        {
            if (Validate(body) is { } error) return Results.BadRequest(new { error });

            var doc = ToDocument(body);
            doc.Id = Guid.NewGuid().ToString();
            doc.CreatedAt = doc.UpdatedAt = DateTime.UtcNow;
            doc.Status = null; // status is trainer-owned

            if (await TargetTaken(db, doc.TargetKey, excludeId: null))
                return Results.Conflict(new { error = $"a task for '{doc.TargetCapability}' already exists" });

            await EnsureIndexAsync(db);
            await Tasks(db).InsertOneAsync(doc);
            return Results.Created($"/api/ml/tasks/{doc.Id}", ToTask(doc));
        });

        group.MapPut("/{id}", async (string id, MlTask body, IMongoDatabase db) =>
        {
            if (Validate(body) is { } error) return Results.BadRequest(new { error });

            var existing = await Tasks(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (existing is null) return Results.NotFound();

            var doc = ToDocument(body);
            doc.Id = id;
            doc.CreatedAt = existing.CreatedAt;
            doc.UpdatedAt = DateTime.UtcNow;
            doc.Status = existing.Status; // status from the body is ignored — it is trainer-owned

            if (await TargetTaken(db, doc.TargetKey, excludeId: id))
                return Results.Conflict(new { error = $"a task for '{doc.TargetCapability}' already exists" });

            await Tasks(db).ReplaceOneAsync(x => x.Id == id, doc);
            return Results.Ok(ToTask(doc));
        });

        // Deleting a task stops training but keeps its registered models serving (Epic 2P decision Р2);
        // removing them from serving means deleting the model versions themselves.
        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var r = await Tasks(db).DeleteOneAsync(x => x.Id == id);
            return r.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Trainer-only: record the outcome of a training attempt. $set of Status alone — never touches the
        // user-editable fields, so there is no lost-update race with a concurrent PUT of the task.
        group.MapPut("/{id}/status", async (string id, MlTaskStatus body, IMongoDatabase db) =>
        {
            var r = await Tasks(db).UpdateOneAsync(
                x => x.Id == id, Builders<MlTaskDocument>.Update.Set(x => x.Status, body));
            return r.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Idempotent first-run seed (called by the AutomationService on startup with its options-derived
        // default task). Inserts ONLY when the ml_tasks collection does not exist yet — once anything has been
        // written the collection exists forever, so a user who deleted all tasks never gets the default back.
        group.MapPost("/seed", async (MlTask body, IMongoDatabase db) =>
        {
            if (Validate(body) is { } error) return Results.BadRequest(new { error });

            var names = await (await db.ListCollectionNamesAsync()).ToListAsync();
            if (names.Contains(Collection)) return Results.NoContent();

            var doc = ToDocument(body);
            doc.Id = string.IsNullOrEmpty(body.Id) ? "default" : body.Id;
            doc.CreatedAt = doc.UpdatedAt = DateTime.UtcNow;
            doc.Status = null;

            await Tasks(db).InsertOneAsync(doc);
            await EnsureIndexAsync(db);
            return Results.Created($"/api/ml/tasks/{doc.Id}", ToTask(doc));
        });
    }

    /// <summary>Validation shared by create/update/seed; returns an error message or null when valid.</summary>
    private static string? Validate(MlTask t)
    {
        if (string.IsNullOrWhiteSpace(t.TargetCapability)) return "targetCapability is required";
        if (t.WindowDays < 1) return "windowDays must be at least 1";
        if (t.MinSamples < 1) return "minSamples must be at least 1";
        if (t.TrainIntervalHours < 1) return "trainIntervalHours must be at least 1";
        if (t.KeepLastVersions < 1) return "keepLastVersions must be at least 1";
        if (t.ZonePromotionMargin < 0) return "zonePromotionMargin must not be negative";
        if (t.ClampMin is { } min && t.ClampMax is { } max && min >= max) return "clampMin must be below clampMax";
        return null;
    }

    private static async Task<bool> TargetTaken(IMongoDatabase db, string targetKey, string? excludeId)
    {
        var b = Builders<MlTaskDocument>.Filter;
        var filter = b.Eq(x => x.TargetKey, targetKey);
        if (excludeId is not null) filter &= b.Ne(x => x.Id, excludeId);
        return await Tasks(db).Find(filter).AnyAsync();
    }

    // Belt-and-braces uniqueness under concurrent writes; the app-level TargetTaken check supplies the
    // friendly 409. Created lazily on the write path only — an index build would create the collection,
    // which must not happen before the seed's "does the collection exist" probe.
    private static async Task EnsureIndexAsync(IMongoDatabase db)
    {
        if (_indexEnsured) return;
        await Tasks(db).Indexes.CreateOneAsync(new CreateIndexModel<MlTaskDocument>(
            Builders<MlTaskDocument>.IndexKeys.Ascending(x => x.TargetKey),
            new CreateIndexOptions { Unique = true }));
        _indexEnsured = true;
    }

    private static MlTaskDocument ToDocument(MlTask t) => new()
    {
        Id = t.Id,
        Name = string.IsNullOrWhiteSpace(t.Name) ? t.TargetCapability.Trim() : t.Name.Trim(),
        TargetCapability = t.TargetCapability.Trim(),
        TargetKey = t.TargetCapability.Trim().ToLowerInvariant(),
        Enabled = t.Enabled,
        WindowDays = t.WindowDays,
        MinSamples = t.MinSamples,
        TrainIntervalHours = t.TrainIntervalHours,
        TrainZoneModels = t.TrainZoneModels,
        ZonePromotionMargin = t.ZonePromotionMargin,
        ClampMin = t.ClampMin,
        ClampMax = t.ClampMax,
        KeepLastVersions = t.KeepLastVersions,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        Status = t.Status,
    };

    private static MlTask ToTask(MlTaskDocument d) => new()
    {
        Id = d.Id, Name = d.Name, TargetCapability = d.TargetCapability, Enabled = d.Enabled,
        WindowDays = d.WindowDays, MinSamples = d.MinSamples, TrainIntervalHours = d.TrainIntervalHours,
        TrainZoneModels = d.TrainZoneModels, ZonePromotionMargin = d.ZonePromotionMargin,
        ClampMin = d.ClampMin, ClampMax = d.ClampMax, KeepLastVersions = d.KeepLastVersions,
        CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt, Status = d.Status,
    };

    private static IMongoCollection<MlTaskDocument> Tasks(IMongoDatabase db) =>
        db.GetCollection<MlTaskDocument>(Collection);
}
