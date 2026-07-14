// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Services;

using Microsoft.Extensions.Logging.Abstractions;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Verifies the roles-model persistence (roadmap Epic 2E) against a real Mongo: the built-in role seeder is
/// idempotent and never clobbers an operator's edits, and the contract Role/User types round-trip through BSON.
/// Mirrors <see cref="ProposalApprovalTests"/> — exercises the same collections the live endpoints use.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")] // needs Docker (Testcontainers); excluded from the unit-only CI job
public sealed class SecuritySeederTests
{
    private readonly InfraFixture _fx;
    public SecuritySeederTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<Role> Roles => _fx.Db.GetCollection<Role>(RolesEndpoints.Collection);
    private IMongoCollection<User> Users => _fx.Db.GetCollection<User>(UsersEndpoints.Collection);

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task Seeder_IsIdempotent_AndDoesNotClobberEdits()
    {
        // Isolate from other tests sharing the collection: run the seeder, then narrow assertions to the built-in ids.
        var seeder = new SecuritySeeder(_fx.Db, NullLogger<SecuritySeeder>.Instance);
        await seeder.StartAsync(default);

        var builtInIds = SecuritySeeder.BuiltInRoles.Select(r => r.Id).ToList();
        foreach (var id in builtInIds)
            Assert.Equal(1, await Roles.CountDocumentsAsync(x => x.Id == id));

        // An operator narrows the guest role; a re-run must not restore the seed permissions.
        await Roles.UpdateOneAsync(
            x => x.Id == "guest",
            Builders<Role>.Update.Set(x => x.Permissions, new List<string>()));

        await seeder.StartAsync(default); // second run

        var guest = await Roles.Find(x => x.Id == "guest").FirstAsync();
        Assert.Empty(guest.Permissions); // edit preserved, not re-seeded
        foreach (var id in builtInIds)
            Assert.Equal(1, await Roles.CountDocumentsAsync(x => x.Id == id)); // still no duplicates
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task User_RoundTripsThroughBson()
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            DisplayName = "Alice",
            Email = "alice@example.com",
            RoleIds = new List<string> { "resident", "guest" },
            Enabled = false,
        };
        await Users.InsertOneAsync(user);

        var stored = await Users.Find(x => x.Id == user.Id).FirstAsync();
        Assert.Equal("Alice", stored.DisplayName);
        Assert.Equal(new[] { "resident", "guest" }, stored.RoleIds);
        Assert.False(stored.Enabled);
    }
}
