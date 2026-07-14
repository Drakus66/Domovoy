// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Seeds the three built-in roles (roadmap Epic 2E) so the roles model is usable out of the box. Idempotent:
/// a built-in role is inserted only when absent, so operator edits to its permissions are never clobbered on
/// restart (mirrors how <see cref="EventInterceptor"/> leaves an existing zone assignment alone). Runs once on
/// startup as a hosted service. <b>No enforcement</b> — this only populates the model; gating requests on these
/// permissions is Phase 3 (local auth).
/// </summary>
public sealed class SecuritySeeder : IHostedService
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<SecuritySeeder> _logger;

    public SecuritySeeder(IMongoDatabase db, ILogger<SecuritySeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// The default roles seeded on first run. Admin holds the super-permission; resident is the everyday
    /// household member; guest can only look. Kept as data (not hard-coded in the seeder body) so it can be
    /// asserted in a unit test without a database.
    /// </summary>
    public static IReadOnlyList<Role> BuiltInRoles { get; } = new[]
    {
        new Role
        {
            Id = "admin",
            Name = "Administrator",
            Description = "Full control of the home, including users and system settings.",
            IsBuiltIn = true,
            Permissions = WellKnownPermissions.All.ToList(),
        },
        new Role
        {
            Id = "resident",
            Name = "Resident",
            Description = "Everyday household member — control devices, modes, automations and approve suggestions.",
            IsBuiltIn = true,
            Permissions = new List<string>
            {
                WellKnownPermissions.DevicesView,
                WellKnownPermissions.DevicesControl,
                WellKnownPermissions.ZonesManage,
                WellKnownPermissions.ModesManage,
                WellKnownPermissions.AutomationsManage,
                WellKnownPermissions.BlocksManage,
                WellKnownPermissions.ProposalsApprove,
                WellKnownPermissions.ActivityView,
            },
        },
        new Role
        {
            Id = "guest",
            Name = "Guest",
            Description = "Can see devices and their state, but cannot change anything.",
            IsBuiltIn = true,
            Permissions = new List<string> { WellKnownPermissions.DevicesView },
        },
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var roles = _db.GetCollection<Role>(RolesEndpoints.Collection);
            foreach (var role in BuiltInRoles)
            {
                // Insert-if-absent: don't overwrite operator edits to a built-in role's permissions.
                var exists = await roles.Find(x => x.Id == role.Id).AnyAsync(cancellationToken);
                if (exists) continue;

                var now = DateTime.UtcNow;
                role.CreatedAt = now;
                role.UpdatedAt = now;
                await roles.InsertOneAsync(role, cancellationToken: cancellationToken);
                _logger.LogInformation("Seeded built-in role {RoleId}", role.Id);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: a missing Mongo at startup must not crash the gateway (offline-first).
            _logger.LogWarning(ex, "Could not seed built-in roles");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
