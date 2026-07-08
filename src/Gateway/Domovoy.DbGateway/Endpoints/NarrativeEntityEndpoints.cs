// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Narrative;
using Domovoy.DbGateway.Services;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for the diary's personalization overrides (roadmap Epic 2N, Phase 3) — user-edited synonym pools
/// merged over the built-in language pack (name the house spirit, the collective «домочадцы», a zone's spoken
/// form). Stored in <c>narrative_entities</c>; every edit invalidates the cached pack so the next render
/// picks it up. The ApiGateway forwards via its HomeStoryController.
/// </summary>
public static class NarrativeEntityEndpoints
{
    public const string Collection = LanguagePackProvider.OverridesCollection;

    /// <summary>Request body for creating/updating an override (id is route/server assigned).</summary>
    public record EntityInput(string Locale, string Kind, string Key, List<SynonymForm> Synonyms);

    public static void MapNarrativeEntityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/narrative-entities").WithTags("NarrativeEntities").WithOpenApi();

        group.MapGet("/", async (string? locale, string? kind, IMongoDatabase db) =>
        {
            var b = Builders<NarrativeEntity>.Filter;
            var filters = new List<FilterDefinition<NarrativeEntity>>();
            if (!string.IsNullOrWhiteSpace(locale)) filters.Add(b.Eq(x => x.Locale, locale));
            if (!string.IsNullOrWhiteSpace(kind)) filters.Add(b.Eq(x => x.Kind, kind));
            var filter = filters.Count == 0 ? FilterDefinition<NarrativeEntity>.Empty : b.And(filters);
            var docs = await Entities(db).Find(filter).ToListAsync();
            return Results.Ok(docs);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var doc = await Entities(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return doc is null ? Results.NotFound() : Results.Ok(doc);
        });

        group.MapPost("/", async (EntityInput input, IMongoDatabase db, LanguagePackProvider packs) =>
        {
            var (entity, error) = Validate(input);
            if (error is not null) return Results.BadRequest(new { error });

            entity!.Id = Guid.NewGuid().ToString();
            await Entities(db).InsertOneAsync(entity);
            packs.Invalidate(entity.Locale);
            return Results.Created($"/api/narrative-entities/{entity.Id}", entity);
        });

        group.MapPut("/{id}", async (string id, EntityInput input, IMongoDatabase db, LanguagePackProvider packs) =>
        {
            var (entity, error) = Validate(input);
            if (error is not null) return Results.BadRequest(new { error });

            var update = Builders<NarrativeEntity>.Update
                .Set(x => x.Locale, entity!.Locale)
                .Set(x => x.Kind, entity.Kind)
                .Set(x => x.Key, entity.Key)
                .Set(x => x.Synonyms, entity.Synonyms)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Entities(db).UpdateOneAsync(x => x.Id == id, update);
            if (result.MatchedCount == 0) return Results.NotFound();
            packs.Invalidate(entity!.Locale);
            return Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db, LanguagePackProvider packs) =>
        {
            var doc = await Entities(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (doc is null) return Results.NotFound();
            await Entities(db).DeleteOneAsync(x => x.Id == id);
            packs.Invalidate(doc.Locale);
            return Results.NoContent();
        });
    }

    private static (NarrativeEntity? entity, string? error) Validate(EntityInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Locale)) return (null, "locale is required");
        if (!NarrativeEntityKinds.All.Contains(input.Kind)) return (null, $"unknown kind '{input.Kind}'");
        if (string.IsNullOrWhiteSpace(input.Key)) return (null, "key is required");
        var synonyms = (input.Synonyms ?? new List<SynonymForm>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();
        if (synonyms.Count == 0) return (null, "at least one synonym with text is required");

        return (new NarrativeEntity
        {
            Locale = input.Locale.Trim().ToLowerInvariant(),
            Kind = input.Kind,
            Key = input.Key.Trim(),
            Synonyms = synonyms,
            UpdatedAt = DateTime.UtcNow,
        }, null);
    }

    private static IMongoCollection<NarrativeEntity> Entities(IMongoDatabase db) =>
        db.GetCollection<NarrativeEntity>(Collection);
}
