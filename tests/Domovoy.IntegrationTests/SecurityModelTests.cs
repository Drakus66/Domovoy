// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Domovoy.DbGateway.Services;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Offline unit tests for the roles model (roadmap Epic 2E): the built-in role definitions and the permission
/// vocabulary. No database — asserts the seed data is internally consistent (every built-in permission is a
/// known one, admin is the superset, guest is read-only).
/// </summary>
public sealed class SecurityModelTests
{
    [Fact]
    public void BuiltInRoles_AreSeededAndAllMarkedBuiltIn()
    {
        var roles = SecuritySeeder.BuiltInRoles;

        // admin / resident / guest + the kiosk panel role (Epic 2O.2).
        Assert.Equal(4, roles.Count);
        Assert.All(roles, r => Assert.True(r.IsBuiltIn));
        Assert.All(roles, r => Assert.False(string.IsNullOrWhiteSpace(r.Id)));
        Assert.Equal(new[] { "admin", "resident", "guest", "kiosk" }, roles.Select(r => r.Id));
    }

    [Fact]
    public void KioskRole_CanViewAndControlDevices_ButManagesNothing()
    {
        var kiosk = SecuritySeeder.BuiltInRoles.Single(r => r.Id == "kiosk");

        Assert.Contains(WellKnownPermissions.DevicesView, kiosk.Permissions);
        Assert.Contains(WellKnownPermissions.DevicesControl, kiosk.Permissions);
        Assert.DoesNotContain(WellKnownPermissions.SystemAdmin, kiosk.Permissions);
        Assert.DoesNotContain(WellKnownPermissions.UsersManage, kiosk.Permissions);
    }

    [Fact]
    public void BuiltInRoles_OnlyReferenceKnownPermissions()
    {
        foreach (var role in SecuritySeeder.BuiltInRoles)
            Assert.All(role.Permissions, p => Assert.Contains(p, WellKnownPermissions.All));
    }

    [Fact]
    public void AdminHoldsEveryPermission_GuestIsReadOnly()
    {
        var admin = SecuritySeeder.BuiltInRoles.Single(r => r.Id == "admin");
        var guest = SecuritySeeder.BuiltInRoles.Single(r => r.Id == "guest");

        Assert.Contains(WellKnownPermissions.SystemAdmin, admin.Permissions);
        Assert.Equal(WellKnownPermissions.All.OrderBy(x => x), admin.Permissions.OrderBy(x => x));

        Assert.Equal(new[] { WellKnownPermissions.DevicesView }, guest.Permissions);
    }

    [Fact]
    public void PermissionVocabulary_HasNoDuplicates()
    {
        Assert.Equal(WellKnownPermissions.All.Count, WellKnownPermissions.All.Distinct().Count());
    }
}
