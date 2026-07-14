// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Blocks;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Persistence for control-block runtime state (roadmap Epic 2Q, Phase 2) in the <c>block_state</c>
/// collection. Kept separate from <see cref="BlockEndpoints"/> (the block <i>config</i>) so a config edit and
/// a live-state snapshot never race on the same document. The AutomationService loads all states at startup
/// and upserts a block's state periodically.
/// </summary>
public static class BlockStateEndpoints
{
    public const string Collection = "block_state";

    public static void MapBlockStateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/block-state").WithTags("ControlBlockState").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
            Results.Ok(await States(db).Find(FilterDefinition<BlockStateRecord>.Empty).ToListAsync()));

        group.MapPut("/{id}", async (string id, BlockStateRecord record, IMongoDatabase db) =>
        {
            record.Id = id;
            record.UpdatedAt = DateTime.UtcNow;
            await States(db).ReplaceOneAsync(x => x.Id == id, record, new ReplaceOptions { IsUpsert = true });
            return Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            await States(db).DeleteOneAsync(x => x.Id == id);
            return Results.NoContent();
        });
    }

    private static IMongoCollection<BlockStateRecord> States(IMongoDatabase db) =>
        db.GetCollection<BlockStateRecord>(Collection);
}
