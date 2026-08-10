// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Read endpoints for the capability device read-model (roadmap Step 4). The WebUI lists/inspects
/// devices through these (the ApiGateway forwards reads here via its CapabilityDevicesController).
/// Discovery writes happen in the EventInterceptor; the only mutation here besides the manual
/// zone/archetype assignments is pruning offline devices from the registry.
/// </summary>
public static class CapabilityDeviceEndpoints
{
    public const string Collection = "capability_devices";

    /// <summary>Request body for binding a device to a zone (empty/null zoneId unassigns).</summary>
    public record ZoneAssignment(string? ZoneId);

    /// <summary>Outcome of <see cref="DeleteOfflineAsync"/> — maps to 204 / 404 / 409.</summary>
    public enum DeleteOutcome { Deleted, NotFound, StillOnline }

    /// <summary>Request body for the manual archetype override (empty/null reverts to the auto value).</summary>
    public record ArchetypeAssignment(string? Archetype);

    /// <summary>Request body for the user-set friendly name (empty/null clears it → UI type-label fallback).</summary>
    public record AliasAssignment(string? Alias);

    /// <summary>Max length of a user alias — generous, but stops an accidental paste from bloating the doc.</summary>
    private const int MaxAliasLength = 120;

    /// <summary>Request body for the device's energy profile (Epic 3C-D); null clears it (back to defaults —
    /// a metered device still counts, an unmetered one stops being estimated).</summary>
    public record EnergyProfileAssignment(EnergyProfile? EnergyProfile);

    /// <summary>Request body for the load-shedding profile (Epic 3C-LM); null ⇒ clear the profile
    /// (device is unmanaged by LoadManager).</summary>
    public record LoadSheddingAssignment(LoadSheddingProfile? LoadShedding);

    private static readonly HashSet<string> LoadSheddingTiers =
        new(StringComparer.Ordinal) { "critical", "sheddable", "unmanaged" };

    public static void MapCapabilityDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/capability-devices")
            .WithTags("CapabilityDevices")
            .WithOpenApi();

        // Словарь архетипов — из контракта, а не из копии в интерфейсе. Копия в WebUI уже отстала на
        // три значения (tariff, person, presence): список выбора не предлагал типов, которые система
        // назначает сама. Образец — RolesEndpoints, отдающий WellKnownPermissions.All.
        group.MapGet("/archetypes", () => Results.Ok(DeviceArchetypes.All));

        // List, optionally filtered by zone (?zoneId=...; empty string returns unassigned devices).
        group.MapGet("/", async (string? zoneId, IMongoDatabase db) =>
        {
            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var filter = zoneId is null
                ? FilterDefinition<CapabilityDeviceDocument>.Empty
                : Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.ZoneId, zoneId);
            var all = await collection.Find(filter).ToListAsync();
            return Results.Ok(all);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var doc = await collection.Find(x => x.Id == id).FirstOrDefaultAsync();
            return doc is null ? Results.NotFound() : Results.Ok(doc);
        });

        // Bind a device to a zone (or unassign with empty/null zoneId). Manual assignment that the
        // discovery path must not clobber — see EventInterceptor (ZoneId is SetOnInsert only).
        group.MapPut("/{id}/zone", async (string id, ZoneAssignment body, IMongoDatabase db) =>
        {
            var zoneId = string.IsNullOrWhiteSpace(body.ZoneId) ? string.Empty : body.ZoneId.Trim();

            if (zoneId.Length > 0)
            {
                var zoneExists = await db.GetCollection<Zone>(ZoneEndpoints.Collection)
                    .Find(z => z.Id == zoneId).AnyAsync();
                if (!zoneExists)
                    return Results.BadRequest(new { error = $"zone '{zoneId}' does not exist" });
            }

            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.ZoneId, zoneId)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Remove a device from the registry. Only offline devices may be deleted (409 otherwise);
        // if the device comes back it re-announces and the discovery upsert recreates the document,
        // so this is how stale/decommissioned devices are pruned without losing re-integration.
        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
            await DeleteOfflineAsync(db, id) switch
            {
                DeleteOutcome.Deleted => Results.NoContent(),
                DeleteOutcome.StillOnline => Results.Conflict(
                    new { error = "device is online; only offline devices can be deleted" }),
                _ => Results.NotFound(),
            });

        // Set/clear the user-set friendly name (Epic 3G-alias). Empty/null clears it, so the UI reverts
        // to a type-derived label / the raw discovery name. A user override the discovery path never writes.
        group.MapPut("/{id}/alias", async (string id, AliasAssignment body, IMongoDatabase db) =>
        {
            var alias = string.IsNullOrWhiteSpace(body.Alias) ? null : body.Alias.Trim();
            if (alias is { Length: > MaxAliasLength })
                alias = alias[..MaxAliasLength];

            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.Alias, alias)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Manual archetype override (Epic 2D). Empty/null reverts to the auto-classified value. The
        // discovery path only writes AutoArchetype, so this override survives re-announces.
        group.MapPut("/{id}/archetype", async (string id, ArchetypeAssignment body, IMongoDatabase db) =>
        {
            var archetype = string.IsNullOrWhiteSpace(body.Archetype) ? null : body.Archetype.Trim().ToLowerInvariant();
            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.Archetype, archetype)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Set/clear the device's energy profile (Epic 3C-D): the accounting toggle, the role that keeps totals
        // honest ('mains' aggregate meter vs a normal consumer) and the nameplate watts used to estimate a
        // device with no meter. A user override the discovery path never writes — see EventInterceptor.
        group.MapPut("/{id}/energy-profile", async (string id, EnergyProfileAssignment body, IMongoDatabase db) =>
        {
            var profile = body.EnergyProfile;
            if (profile is not null)
            {
                var role = string.IsNullOrWhiteSpace(profile.Role) ? null : profile.Role.Trim().ToLowerInvariant();
                if (role is not null && !EnergyEndpoints.Roles.Contains(role))
                    return Results.BadRequest(new { error = "role must be 'consumer', 'mains' or empty" });
                profile.Role = role;
                profile.MaxPowerW = NonNegative(profile.MaxPowerW);
                profile.MinPowerW = NonNegative(profile.MinPowerW);
                profile.StandbyPowerW = NonNegative(profile.StandbyPowerW);
                profile.ScaleCapabilityId = Trimmed(profile.ScaleCapabilityId);
                profile.CircuitId = Trimmed(profile.CircuitId);
            }

            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var device = await collection.Find(x => x.Id == id).FirstOrDefaultAsync();
            if (device is null) return Results.NotFound();

            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.EnergyProfile, profile)
                // The synthetic power/energy capabilities follow the profile, so a tracked device enters the
                // accounting the moment the toggle flips instead of waiting for its next announce.
                .Set(x => x.Capabilities, SyntheticCapabilities.Apply(device.Capabilities, profile))
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            await collection.UpdateOneAsync(x => x.Id == id, update);
            return Results.NoContent();
        });

        // Set/clear the device's load-shedding profile (Epic 3C-LM). A user override the discovery path
        // never writes — see EventInterceptor. Null clears it (device becomes unmanaged again).
        group.MapPut("/{id}/load-shedding", async (string id, LoadSheddingAssignment body, IMongoDatabase db) =>
        {
            var profile = body.LoadShedding;
            if (profile is not null)
            {
                if (string.IsNullOrWhiteSpace(profile.ControlCapabilityId))
                    return Results.BadRequest(new { error = "controlCapabilityId is required" });
                if (profile.ModeTier.Values.Any(t => !LoadSheddingTiers.Contains(t)))
                    return Results.BadRequest(new { error = "modeTier values must be 'critical', 'sheddable' or 'unmanaged'" });
            }

            var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.LoadShedding, profile)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static double? NonNegative(double? v) => v is { } d ? Math.Max(0, d) : null;

    private static string? Trimmed(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>
    /// Deletes a device document only while it is offline — the filter carries the IsOnline guard so
    /// the check-and-delete is a single atomic operation (no race with a concurrent state report).
    /// </summary>
    public static async Task<DeleteOutcome> DeleteOfflineAsync(IMongoDatabase db, string id)
    {
        var collection = db.GetCollection<CapabilityDeviceDocument>(Collection);
        var result = await collection.DeleteOneAsync(x => x.Id == id && !x.IsOnline);
        if (result.DeletedCount > 0)
            return DeleteOutcome.Deleted;
        var exists = await collection.Find(x => x.Id == id).AnyAsync();
        return exists ? DeleteOutcome.StillOnline : DeleteOutcome.NotFound;
    }
}
