// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Ml;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// The ML-activity journal (roadmap Epic 3I) — the visible "pulse" of the proactive layer, in the
/// <c>ml_activity</c> collection. Written by the AutomationService's trainer and proposers after each cycle,
/// read by the ML page's Journal section. A TTL index on <see cref="MlActivityEntry.Timestamp"/> bounds the
/// journal (60-day retention), so it never grows without limit — the pulse is a recent, self-pruning feed, not
/// an audit log. Kept separate from the operational logs so a household that ignores ML never sees it there.
/// </summary>
public static class MlActivityEndpoints
{
    public const string Collection = "ml_activity";

    private const int RetentionDays = 60;
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public static void MapMlActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ml/activity").WithTags("ML activity").WithOpenApi();

        // Newest-first, optionally filtered by source; ?limit caps the page (default 100).
        group.MapGet("/", async (string? source, int? limit, IMongoDatabase db) =>
        {
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var filter = string.IsNullOrWhiteSpace(source)
                ? FilterDefinition<MlActivityEntry>.Empty
                : Builders<MlActivityEntry>.Filter.Eq(x => x.Source, source);
            var rows = await Activity(db).Find(filter).SortByDescending(x => x.Timestamp).Limit(take).ToListAsync();
            return Results.Ok(rows);
        });

        // Append one entry (from the AutomationService contours). Server-assigns the id; stamps the time if unset.
        group.MapPost("/", async (MlActivityEntry entry, IMongoDatabase db) =>
        {
            await EnsureTtlIndexAsync(db);
            entry.Id = Guid.NewGuid().ToString();
            if (entry.Timestamp == default) entry.Timestamp = DateTime.UtcNow;
            await Activity(db).InsertOneAsync(entry);
            return Results.Created($"/api/ml/activity/{entry.Id}", entry);
        });
    }

    // Idempotent — creating an index that already exists (same keys + options) is a no-op on the server.
    private static Task EnsureTtlIndexAsync(IMongoDatabase db)
    {
        var model = new CreateIndexModel<MlActivityEntry>(
            Builders<MlActivityEntry>.IndexKeys.Ascending(x => x.Timestamp),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(RetentionDays) });
        return Activity(db).Indexes.CreateOneAsync(model);
    }

    private static IMongoCollection<MlActivityEntry> Activity(IMongoDatabase db) =>
        db.GetCollection<MlActivityEntry>(Collection);
}
