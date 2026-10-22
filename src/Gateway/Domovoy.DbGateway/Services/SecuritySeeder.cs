// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Seeds the three built-in roles (roadmap Epic 2E) so the roles model is usable out of the box, and — now that
/// authentication is enforced (mobile-app / remote-access track) — bootstraps an initial <c>admin</c> login user
/// so the household can actually sign in on first run. Idempotent: a built-in role is inserted only when absent
/// (operator edits are never clobbered), and the admin user is created only when the collection has no users at
/// all, so it never resurrects a deleted account or overwrites a changed password.
/// </summary>
public sealed class SecuritySeeder : IHostedService
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<SecuritySeeder> _logger;
    private readonly IConfiguration _config;

    public SecuritySeeder(IMongoDatabase db, ILogger<SecuritySeeder> logger, IConfiguration config)
    {
        _db = db;
        _logger = logger;
        _config = config;
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
        new Role
        {
            Id = "kiosk",
            Name = "Kiosk panel",
            Description = "A wall panel: view and control devices from its dashboard, nothing else (Epic 2O.2).",
            IsBuiltIn = true,
            Permissions = new List<string>
            {
                WellKnownPermissions.DevicesView,
                WellKnownPermissions.DevicesControl,
            },
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

            await SeedAdminUserAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: a missing Mongo at startup must not crash the gateway (offline-first).
            _logger.LogWarning(ex, "Could not seed built-in roles / admin user");
        }
    }

    /// <summary>
    /// Create the initial <c>admin</c> login user when no <b>login-capable</b> account exists yet, so a fresh
    /// install can sign in. "Login-capable" = a user with a username; this still fires on an empty database and
    /// also recovers one that only holds identity-only users (Epic 2E users created before auth, which have no
    /// username/password) — otherwise those stragglers would make the collection "non-empty" and leave nobody
    /// able to sign in. The password comes from <c>Security:BootstrapAdminPassword</c> (env
    /// <c>SECURITY__BOOTSTRAPADMINPASSWORD</c>); if unset it defaults to <c>admin</c> and we log a loud warning to
    /// change it before exposing the system. Once a login-capable user exists this is a no-op.
    /// </summary>
    private async Task SeedAdminUserAsync(CancellationToken cancellationToken)
    {
        var users = _db.GetCollection<User>(UsersEndpoints.Collection);
        var loginCapableExists = await users
            .Find(u => u.Username != null && u.Username != "")
            .AnyAsync(cancellationToken);
        if (loginCapableExists) return;

        var configured = _config["Security:BootstrapAdminPassword"];
        var password = string.IsNullOrWhiteSpace(configured) ? "admin" : configured;

        var now = DateTime.UtcNow;
        var admin = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "admin",
            DisplayName = "Administrator",
            PasswordHash = PasswordHasher.Hash(password),
            RoleIds = new List<string> { "admin" },
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await users.InsertOneAsync(admin, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(configured))
            _logger.LogWarning("Seeded initial admin user with the DEFAULT password 'admin'. " +
                "Set Security:BootstrapAdminPassword (env SECURITY__BOOTSTRAPADMINPASSWORD) and change it before remote exposure.");
        else
            _logger.LogInformation("Seeded initial admin user 'admin' with the configured bootstrap password.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
