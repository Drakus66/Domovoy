// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for user automation rules (roadmap Epic 1A) in the <c>automations</c> collection, plus run
/// history (<c>auto_history</c>). The AutomationService loads these over HTTP and the WebUI manages
/// them via the ApiGateway proxy. Safety-floor (protected) rules are NOT stored here — they live in
/// the AutomationService's local config and cannot be created/edited through this API, so the API
/// always forces <see cref="AutomationRule.IsProtected"/> to false.
/// </summary>
public static class AutomationEndpoints
{
    public const string Collection = "automations";
    public const string HistoryCollection = "auto_history";

    public record StatusUpdate(RuleStatus Status);

    public static void MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/automations").WithTags("Automations").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var rules = await Rules(db).Find(FilterDefinition<AutomationRule>.Empty).ToListAsync();
            return Results.Ok(rules);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var rule = await Rules(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        });

        group.MapPost("/", async (AutomationRule rule, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
                return Results.BadRequest(new { error = "rule name is required" });

            rule.Id = Guid.NewGuid().ToString();
            rule.IsProtected = false; // safety-floor rules are config-only, never created via API
            rule.CreatedAt = rule.UpdatedAt = DateTime.UtcNow;
            await Rules(db).InsertOneAsync(rule);
            return Results.Created($"/api/automations/{rule.Id}", rule);
        });

        group.MapPut("/{id}", async (string id, AutomationRule rule, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
                return Results.BadRequest(new { error = "rule name is required" });

            var existing = await Rules(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (existing is null) return Results.NotFound();

            rule.Id = id;
            rule.IsProtected = false;
            rule.CreatedAt = existing.CreatedAt;
            rule.UpdatedAt = DateTime.UtcNow;
            await Rules(db).ReplaceOneAsync(x => x.Id == id, rule);
            return Results.NoContent();
        });

        // Quick enable/disable without re-sending the whole rule (UI toggle).
        group.MapPut("/{id}/status", async (string id, StatusUpdate body, IMongoDatabase db) =>
        {
            var update = Builders<AutomationRule>.Update
                .Set(x => x.Status, body.Status)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var result = await Rules(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Rules(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Run history, newest first; optional ?ruleId= filter and ?limit=.
        group.MapGet("/history", async (string? ruleId, int? limit, IMongoDatabase db) =>
        {
            var filter = string.IsNullOrEmpty(ruleId)
                ? FilterDefinition<AutoHistory>.Empty
                : Builders<AutoHistory>.Filter.Eq(x => x.RuleId, ruleId);
            var rows = await History(db)
                .Find(filter)
                .SortByDescending(x => x.Timestamp)
                .Limit(Math.Clamp(limit ?? 100, 1, 1000))
                .ToListAsync();
            return Results.Ok(rows);
        });
    }

    private static IMongoCollection<AutomationRule> Rules(IMongoDatabase db) =>
        db.GetCollection<AutomationRule>(Collection);

    private static IMongoCollection<AutoHistory> History(IMongoDatabase db) =>
        db.GetCollection<AutoHistory>(HistoryCollection);
}
