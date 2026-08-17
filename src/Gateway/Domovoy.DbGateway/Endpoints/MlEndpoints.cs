// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Ml;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Model registry (roadmap Epic 2A) in the <c>ml_models</c> collection. The AutomationService trainer
/// registers trained models here (metadata + serialized artifact); the ML block fetches the latest
/// artifact for inference. Metadata in Mongo, artifact inline as BSON binary (small for the v1 regression).
/// </summary>
public static class MlEndpoints
{
    public const string Collection = "ml_models";

    /// <summary>
    /// Control-block parameter holding a pinned model version (Epic 2C <c>model_selection</c>): a person decided
    /// this instance serves exactly that version. Retention must not delete it behind their back.
    /// </summary>
    public const string ModelVersionParam = "model_version";

    /// <summary>Register payload: metadata + base64-encoded ML.NET artifact.</summary>
    public record RegisterRequest(MlModel Model, string ArtifactBase64);

    /// <summary>Prune payload (Epic 2P retention): versions kept per (kind, target, scope) line; optional target filter.</summary>
    public record PruneRequest(int KeepLast, string? Target);

    public static void MapMlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ml").WithTags("ML").WithOpenApi();

        // List model metadata (artifact projected out), newest first.
        group.MapGet("/models", async (IMongoDatabase db) =>
        {
            var docs = await Models(db).Find(FilterDefinition<MlModelDocument>.Empty)
                .SortByDescending(x => x.TrainedAt).ToListAsync();
            return Results.Ok(docs.Select(ToMetadata));
        });

        // Latest registered model for a (kind, target, scope), or 404. Scope (Epic 2I) defaults to global;
        // a caller resolving the zone→zone_kind→global chain queries each level until one returns a model.
        group.MapGet("/models/latest", async (string? kind, string? target, string? level, string? key, IMongoDatabase db) =>
        {
            var b = Builders<MlModelDocument>.Filter;
            var filters = new List<FilterDefinition<MlModelDocument>>();
            if (!string.IsNullOrEmpty(kind)) filters.Add(b.Eq(x => x.Kind, kind));
            if (!string.IsNullOrEmpty(target)) filters.Add(b.Eq(x => x.TargetCapability, target));
            if (!string.IsNullOrEmpty(level)) filters.Add(b.Eq(x => x.Scope.Level, level));
            if (!string.IsNullOrEmpty(key)) filters.Add(b.Eq(x => x.Scope.Key, key));
            var filter = filters.Count == 0 ? FilterDefinition<MlModelDocument>.Empty : b.And(filters);

            var doc = await Models(db).Find(filter).SortByDescending(x => x.Version).FirstOrDefaultAsync();
            return doc is null ? Results.NotFound() : Results.Ok(ToMetadata(doc));
        });

        // Download the serialized artifact for inference.
        group.MapGet("/models/{id}/artifact", async (string id, IMongoDatabase db) =>
        {
            var doc = await Models(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return doc is null
                ? Results.NotFound()
                : Results.Bytes(doc.Artifact, "application/octet-stream");
        });

        // Delete a single model version (Epic 2P retention). The UI warns when the version is pinned by a
        // block (`model_version` param) — a deleted pin falls back to the scope's latest on the next refresh.
        group.MapDelete("/models/{id}", async (string id, IMongoDatabase db) =>
        {
            var r = await Models(db).DeleteOneAsync(x => x.Id == id);
            return r.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Prune old versions across (kind, target, scope) lines, keeping the N most recent of each (Epic 2P) and
        // never a version a control block pins (Epic 2C) — see PruneAsync.
        group.MapPost("/models/prune", async (PruneRequest req, IMongoDatabase db, ILoggerFactory loggers) =>
        {
            if (req.KeepLast < 1) return Results.BadRequest(new { error = "keepLast must be at least 1" });

            var b = Builders<MlModelDocument>.Filter;
            var filter = string.IsNullOrEmpty(req.Target)
                ? FilterDefinition<MlModelDocument>.Empty
                : b.Eq(x => x.TargetCapability, req.Target);

            var deleted = await PruneAsync(db, filter, req.KeepLast, loggers.CreateLogger(nameof(MlEndpoints)));
            return Results.Ok(new { deleted });
        });

        // Register a freshly trained model (from the AutomationService trainer). `keepLast` > 0 auto-prunes
        // the registered model's own (kind, target, scope) line right after the insert (Epic 2P retention).
        group.MapPost("/models", async (RegisterRequest req, int? keepLast, IMongoDatabase db, ILoggerFactory loggers) =>
        {
            if (req.Model is null || string.IsNullOrEmpty(req.ArtifactBase64))
                return Results.BadRequest(new { error = "model and artifact are required" });

            var model = req.Model;
            model.Id = Guid.NewGuid().ToString();
            model.TrainedAt = DateTime.UtcNow;
            model.Scope ??= ModelScope.Global;

            // Monotonic version per (kind, target, scope) — each scope has its own version line (Epic 2I).
            var b = Builders<MlModelDocument>.Filter;
            var prev = await Models(db)
                .Find(b.And(
                    b.Eq(x => x.Kind, model.Kind),
                    b.Eq(x => x.TargetCapability, model.TargetCapability),
                    b.Eq(x => x.Scope.Level, model.Scope.Level),
                    b.Eq(x => x.Scope.Key, model.Scope.Key)))
                .SortByDescending(x => x.Version).FirstOrDefaultAsync();
            model.Version = (prev?.Version ?? 0) + 1;

            var doc = new MlModelDocument
            {
                Id = model.Id, Name = model.Name, Kind = model.Kind, TargetCapability = model.TargetCapability,
                Scope = model.Scope, Version = model.Version, TrainedAt = model.TrainedAt,
                SampleCount = model.SampleCount, Rmse = model.Rmse,
                HoldoutMae = model.HoldoutMae, HoldoutSampleCount = model.HoldoutSampleCount,
                HoldoutScore = model.HoldoutScore, Metric = model.Metric, Features = model.Features,
                Algorithm = model.Algorithm,
                Artifact = Convert.FromBase64String(req.ArtifactBase64),
            };
            await Models(db).InsertOneAsync(doc);

            if (keepLast is > 0)
            {
                var line = b.And(
                    b.Eq(x => x.Kind, model.Kind),
                    b.Eq(x => x.TargetCapability, model.TargetCapability),
                    b.Eq(x => x.Scope.Level, model.Scope.Level),
                    b.Eq(x => x.Scope.Key, model.Scope.Key));
                await PruneAsync(db, line, keepLast.Value, loggers.CreateLogger(nameof(MlEndpoints)));
            }

            return Results.Created($"/api/ml/models/{doc.Id}", ToMetadata(doc));
        });
    }

    /// <summary>
    /// Delete everything older than the <paramref name="keepLast"/> most recent versions of each
    /// (kind, target, scope) line matching <paramref name="filter"/>. Metadata-only scan (artifact projected
    /// out), then a single DeleteMany by id. Public so retention tests exercise it against a real Mongo.
    ///
    /// <para><b>Pin-aware</b> (Epic 2C): a version pinned by a control block's <see cref="ModelVersionParam"/>
    /// is never deleted, even when it falls outside the retention window. A pin is a person's decision that this
    /// instance serves exactly that version; retention silently deleting it would drop the instance back to
    /// "latest" — the opposite of what they asked for, and invisible until behaviour changed. Keeping a pinned
    /// version means a line can hold slightly more than <paramref name="keepLast"/> versions; that is the
    /// intended trade, and it is logged.</para>
    /// </summary>
    public static async Task<long> PruneAsync(
        IMongoDatabase db, FilterDefinition<MlModelDocument> filter, int keepLast, ILogger? logger = null)
    {
        var metas = await Models(db).Find(filter)
            .Project(x => new { x.Id, x.Kind, x.TargetCapability, x.Scope, x.Version })
            .ToListAsync();

        var pins = await LoadPinnedVersionsAsync(db);
        var expired = metas
            .GroupBy(m => (m.Kind, Target: m.TargetCapability.ToLowerInvariant(), Key: (m.Scope ?? ModelScope.Global).AsKey()))
            .SelectMany(g => g.OrderByDescending(m => m.Version).Skip(keepLast))
            .ToList();

        var stale = expired
            .Where(m => !IsPinned(pins, m.TargetCapability, m.Version))
            .Select(m => m.Id)
            .ToList();

        var kept = expired.Count - stale.Count;
        if (kept > 0)
            logger?.LogInformation("Retention kept {Count} expired model version(s) pinned by a control block", kept);
        if (stale.Count == 0) return 0;

        var r = await Models(db).DeleteManyAsync(Builders<MlModelDocument>.Filter.In(x => x.Id, stale));
        return r.DeletedCount;
    }

    /// <summary>
    /// Model versions pinned by control blocks, as (target, version) pairs. A governor block names its ML target
    /// through its measured input (the port id and its capability id are the same capability — see the apply
    /// wizard), so the pin can be scoped to that target's lines. A pinned block with no readable input protects
    /// the version number on every line (target <c>""</c>): over-keeping a version is recoverable, deleting a
    /// pinned one is not. Scope is deliberately not matched — which scope in the zone→zone_kind→global chain ends
    /// up serving is a runtime decision, so all scopes of the target keep the version.
    /// </summary>
    private static async Task<HashSet<(string Target, int Version)>> LoadPinnedVersionsAsync(IMongoDatabase db)
    {
        var pins = new HashSet<(string, int)>();
        var blocks = await db.GetCollection<ControlBlock>(BlockEndpoints.Collection)
            .Find(FilterDefinition<ControlBlock>.Empty).ToListAsync();

        foreach (var block in blocks)
        {
            // Disabled instances count too: re-enabling one must not find its pinned version gone.
            if (!block.Params.TryGetValue(ModelVersionParam, out var raw)) continue;
            var version = (int)Math.Round(raw);
            if (version <= 0) continue; // 0 = "serve latest", nothing to protect

            var targets = block.Inputs
                .SelectMany(i => new[] { i.Key, i.Value?.CapabilityId })
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (targets.Count == 0) pins.Add((string.Empty, version));
            else foreach (var target in targets) pins.Add((target, version));
        }
        return pins;
    }

    private static bool IsPinned(HashSet<(string Target, int Version)> pins, string target, int version) =>
        pins.Count > 0
        && (pins.Contains((target.ToLowerInvariant(), version)) || pins.Contains((string.Empty, version)));

    private static MlModel ToMetadata(MlModelDocument d) => new()
    {
        Id = d.Id, Name = d.Name, Kind = d.Kind, TargetCapability = d.TargetCapability,
        Scope = d.Scope ?? ModelScope.Global, Version = d.Version, TrainedAt = d.TrainedAt,
        SampleCount = d.SampleCount, Rmse = d.Rmse,
        HoldoutMae = d.HoldoutMae, HoldoutSampleCount = d.HoldoutSampleCount,
        HoldoutScore = d.HoldoutScore, Metric = d.Metric, Features = d.Features,
        Algorithm = d.Algorithm,
    };

    private static IMongoCollection<MlModelDocument> Models(IMongoDatabase db) =>
        db.GetCollection<MlModelDocument>(Collection);
}
