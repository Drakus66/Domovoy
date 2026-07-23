// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for global variables (roadmap Epic 3E, Hubitat "Hub Variables") in the <c>variables</c> collection.
/// A variable's config (name/type/description) and its live value share one document — see
/// <see cref="GlobalVariable"/>. The value-only PUT exists so the AutomationService (which persists a
/// variable's value whenever its virtual device is commanded) never has to round-trip a stale copy of
/// Name/Type/Description just to save a new value.
/// </summary>
public static class VariableEndpoints
{
    public const string Collection = "variables";

    public record ValueUpdate(object? Value);

    public static void MapVariableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/variables").WithTags("Variables").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var variables = await Variables(db).Find(FilterDefinition<GlobalVariable>.Empty).ToListAsync();
            return Results.Ok(variables);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var variable = await Variables(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return variable is null ? Results.NotFound() : Results.Ok(variable);
        });

        group.MapPost("/", async (GlobalVariable variable, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
                return Results.BadRequest(new { error = "variable name is required" });

            variable.Id = Guid.NewGuid().ToString();
            variable.Value = AutomationEndpoints.ToPlain(variable.Value);
            variable.CreatedAt = variable.UpdatedAt = DateTime.UtcNow;
            await Variables(db).InsertOneAsync(variable);
            return Results.Created($"/api/variables/{variable.Id}", variable);
        });

        group.MapPut("/{id}", async (string id, GlobalVariable variable, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
                return Results.BadRequest(new { error = "variable name is required" });

            var existing = await Variables(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (existing is null) return Results.NotFound();

            variable.Id = id;
            variable.Value = AutomationEndpoints.ToPlain(variable.Value);
            variable.CreatedAt = existing.CreatedAt;
            variable.UpdatedAt = DateTime.UtcNow;
            await Variables(db).ReplaceOneAsync(x => x.Id == id, variable);
            return Results.NoContent();
        });

        // Value-only update: the AutomationService writes here when a variable's virtual device is
        // commanded, so it must NOT clobber Name/Type/Description with a stale copy.
        group.MapPut("/{id}/value", async (string id, ValueUpdate body, IMongoDatabase db) =>
        {
            var update = Builders<GlobalVariable>.Update
                .Set(x => x.Value, AutomationEndpoints.ToPlain(body.Value))
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var result = await Variables(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Variables(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static IMongoCollection<GlobalVariable> Variables(IMongoDatabase db) =>
        db.GetCollection<GlobalVariable>(Collection);
}
