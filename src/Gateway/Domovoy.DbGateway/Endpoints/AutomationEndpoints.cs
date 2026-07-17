// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Automations;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for user automation rules (roadmap Epic 1A) in the <c>automations</c> collection, plus run
/// history (<c>auto_history</c>). The AutomationService loads these over HTTP and the WebUI manages
/// them via the ApiGateway proxy. Every rule the engine runs is one of these — there is no hardcoded
/// rule floor — so the API always forces the legacy <see cref="AutomationRule.IsProtected"/> flag to false.
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

            NormalizeJsonValues(rule);
            rule.Id = Guid.NewGuid().ToString();
            rule.IsProtected = false; // legacy flag: no rule is protected; pin it false regardless of input
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

            NormalizeJsonValues(rule);
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

    /// <summary>
    /// HTTP binding leaves the <c>object</c>-typed comparison/command values as <see cref="JsonElement"/>,
    /// which the Mongo <c>ObjectSerializer</c> (deliberately) refuses to persist. Flatten them to BCL
    /// primitives at the boundary so rules store as plain BSON regardless of who posted them (WebUI,
    /// RuleSuggester, DiscoveryEngine, scripts). Public so tests can exercise the exact endpoint logic.
    /// </summary>
    public static void NormalizeJsonValues(AutomationRule rule)
    {
        foreach (var t in rule.Triggers) t.Value = ToPlain(t.Value);
        foreach (var c in rule.Conditions) c.Value = ToPlain(c.Value);
        foreach (var a in rule.Actions)
        {
            if (a.Set is null) continue;
            foreach (var key in a.Set.Keys.ToList()) a.Set[key] = ToPlain(a.Set[key]);
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

    private static IMongoCollection<AutomationRule> Rules(IMongoDatabase db) =>
        db.GetCollection<AutomationRule>(Collection);

    private static IMongoCollection<AutoHistory> History(IMongoDatabase db) =>
        db.GetCollection<AutoHistory>(HistoryCollection);
}
