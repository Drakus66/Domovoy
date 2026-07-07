// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.Contracts.Messaging;
using Domovoy.DbGateway.Models;
using Domovoy.MessageBus;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Home mode / presence context (roadmap Epic 1G). The DbGateway owns the persisted current mode (a
/// single document in <c>home_state</c>). A mode change — whether a manual switch from the UI or a
/// presence-driven switch from the AutomationService — goes through <c>PUT /api/mode</c>, which upserts
/// the document and publishes <see cref="HomeModeChangedV1"/> on the bus. Consumers (the AutomationService
/// rule engine and this gateway's own EventInterceptor) react to that event; the explicit
/// <c>mode_change</c> event-log record and the per-event mode stamping are written by the EventInterceptor,
/// keeping all event-log writes in one place.
/// </summary>
public static class ModeEndpoints
{
    public const string Collection = "home_state";

    public record ModeUpdate(string Mode, string? Source);

    public static void MapModeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mode").WithTags("HomeMode").WithOpenApi();

        // Current mode (defaults to Home if never set).
        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var state = await GetOrDefault(db);
            return Results.Ok(state);
        });

        // Well-known modes for UI selection (open set — deployments may surface custom modes too).
        group.MapGet("/options", () => Results.Ok(WellKnownModes.All));

        // Set the mode (manual UI switch or presence-driven). Idempotent: a no-op switch still returns the
        // current state but does not re-publish, so consumers and the event-log don't see phantom changes.
        group.MapPut("/", async (ModeUpdate body, IMongoDatabase db, IMessageBus bus, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Mode))
                return Results.BadRequest(new { error = "mode is required" });

            var mode = body.Mode.Trim();
            var source = string.IsNullOrWhiteSpace(body.Source) ? ModeChangeSources.User : body.Source.Trim();

            var existing = await GetOrDefault(db);
            var previous = existing.Mode;
            var changed = !string.Equals(previous, mode, StringComparison.OrdinalIgnoreCase);

            var state = new HomeState { Mode = mode, Source = source, UpdatedAt = DateTime.UtcNow };
            await States(db).ReplaceOneAsync(
                x => x.Id == HomeState.SingletonId, state, new ReplaceOptions { IsUpsert = true });

            if (changed)
            {
                var envelope = Envelope<HomeModeChangedV1>.Create(
                    MessageTypes.HomeModeChanged,
                    source: $"db-gateway:{source}",
                    data: new HomeModeChangedV1(mode, previous, source, state.UpdatedAt),
                    subject: mode);
                await bus.PublishAsync(BusTopology.EventsExchange, BusTopology.HomeModeChangedKey, envelope, ct);
            }

            return Results.Ok(state);
        });
    }

    private static async Task<HomeState> GetOrDefault(IMongoDatabase db)
    {
        var state = await States(db).Find(x => x.Id == HomeState.SingletonId).FirstOrDefaultAsync();
        return state ?? new HomeState();
    }

    private static IMongoCollection<HomeState> States(IMongoDatabase db) =>
        db.GetCollection<HomeState>(Collection);
}
