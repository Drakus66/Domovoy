// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for the electrical topology (roadmap Epic 3C-D) — supplies, panels and circuits, the tree devices
/// hang off through <c>EnergyProfile.CircuitId</c>. Shaped like <see cref="ZoneEndpoints"/>: a flat collection
/// with parent links, resolved into a tree by the client. Deleting a node reparents its children and detaches
/// the devices that pointed at it, so the topology never leaves dangling references behind.
/// </summary>
public static class PowerTopologyEndpoints
{
    public const string Collection = "power_topology";

    /// <summary>Request body for creating/updating a node (id is route/server assigned).</summary>
    public record PowerNodeInput(
        string Name, string Kind, string? ParentId, string? Phase, double? BreakerAmps, double? Voltage,
        string? MeterDeviceId, string? PowerSourceKind, int Order = 0);

    public static void MapPowerTopologyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/power-topology").WithTags("PowerTopology").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
            Results.Ok(await Nodes(db).Find(FilterDefinition<PowerNode>.Empty).ToListAsync()));

        group.MapPost("/", async (PowerNodeInput input, IMongoDatabase db) =>
        {
            if (Validate(input) is { } error) return Results.BadRequest(new { error });

            var now = DateTime.UtcNow;
            var node = new PowerNode
            {
                Id = Guid.NewGuid().ToString(),
                Name = input.Name.Trim(),
                Kind = input.Kind,
                ParentId = Blank(input.ParentId),
                Phase = Blank(input.Phase),
                BreakerAmps = NonNegative(input.BreakerAmps),
                Voltage = NonNegative(input.Voltage),
                MeterDeviceId = Blank(input.MeterDeviceId),
                PowerSourceKind = Blank(input.PowerSourceKind),
                Order = input.Order,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await Nodes(db).InsertOneAsync(node);
            return Results.Created($"/api/power-topology/{node.Id}", node);
        });

        group.MapPut("/{id}", async (string id, PowerNodeInput input, IMongoDatabase db) =>
        {
            if (Validate(input) is { } error) return Results.BadRequest(new { error });
            if (Blank(input.ParentId) == id) return Results.BadRequest(new { error = "a node cannot be its own parent" });

            var update = Builders<PowerNode>.Update
                .Set(x => x.Name, input.Name.Trim())
                .Set(x => x.Kind, input.Kind)
                .Set(x => x.ParentId, Blank(input.ParentId))
                .Set(x => x.Phase, Blank(input.Phase))
                .Set(x => x.BreakerAmps, NonNegative(input.BreakerAmps))
                .Set(x => x.Voltage, NonNegative(input.Voltage))
                .Set(x => x.MeterDeviceId, Blank(input.MeterDeviceId))
                .Set(x => x.PowerSourceKind, Blank(input.PowerSourceKind))
                .Set(x => x.Order, input.Order)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Nodes(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var node = await Nodes(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (node is null) return Results.NotFound();

            // Keep the tree connected: children move up to the deleted node's parent.
            await Nodes(db).UpdateManyAsync(
                x => x.ParentId == id,
                Builders<PowerNode>.Update.Set(x => x.ParentId, node.ParentId).Set(x => x.UpdatedAt, DateTime.UtcNow));

            // Devices on this circuit go back to "not mapped" rather than pointing at a node that is gone.
            var devices = db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);
            await devices.UpdateManyAsync(
                Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.EnergyProfile!.CircuitId, id),
                Builders<CapabilityDeviceDocument>.Update
                    .Set(x => x.EnergyProfile!.CircuitId, null)
                    .Set(x => x.LastUpdated, DateTime.UtcNow));

            await Nodes(db).DeleteOneAsync(x => x.Id == id);
            return Results.NoContent();
        });
    }

    /// <summary>Reject a malformed node up front; null ⇒ the input is usable.</summary>
    private static string? Validate(PowerNodeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "node name is required";
        if (!PowerNodeKinds.IsKnown(input.Kind)) return "kind must be 'supply', 'panel' or 'circuit'";
        if (Blank(input.Phase) is { } phase && !PowerPhases.IsKnown(phase))
            return "phase must be 'l1', 'l2', 'l3', 'three' or empty";
        return null;
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static double? NonNegative(double? v) => v is { } d ? Math.Max(0, d) : null;

    private static IMongoCollection<PowerNode> Nodes(IMongoDatabase db) =>
        db.GetCollection<PowerNode>(Collection);
}
