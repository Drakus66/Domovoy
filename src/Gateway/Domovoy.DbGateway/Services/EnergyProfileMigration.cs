// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// One-shot migration of the Epic 3C <c>EnergyRole</c> field into the per-device <see cref="EnergyProfile"/>
/// (Epic 3C-D): <c>mains</c> becomes the profile's role, <c>excluded</c> becomes the accounting toggle turned
/// off, and the legacy field is unset. Idempotent — once no document carries the old field it does nothing,
/// so it is safe to run on every startup.
/// </summary>
public static class EnergyProfileMigration
{
    public static async Task RunAsync(IMongoDatabase db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var collection = db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);
            var legacy = Builders<CapabilityDeviceDocument>.Filter.Ne<string?>(x => x.EnergyRole, null);
            var devices = await collection.Find(legacy).ToListAsync(ct);
            if (devices.Count == 0) return;

            foreach (var device in devices)
            {
                var profile = device.EnergyProfile ?? new EnergyProfile();
                switch (device.EnergyRole)
                {
                    case EnergyEndpoints.MainsRole:
                        profile.Role = EnergyEndpoints.MainsRole;
                        break;
                    case "excluded":
                        profile.Track = false;
                        break;
                }

                var update = Builders<CapabilityDeviceDocument>.Update
                    .Set(x => x.EnergyProfile, profile)
                    .Unset(x => x.EnergyRole);
                await collection.UpdateOneAsync(x => x.Id == device.Id, update, cancellationToken: ct);
            }

            logger.LogInformation("Migrated {Count} device(s) from energyRole to the energy profile", devices.Count);
        }
        catch (Exception ex)
        {
            // A failed migration must not stop the gateway: the legacy field simply stays and is retried next start.
            logger.LogWarning(ex, "Energy-role migration did not complete");
        }
    }
}
