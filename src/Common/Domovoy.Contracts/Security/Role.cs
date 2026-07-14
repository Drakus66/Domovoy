// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Security;

/// <summary>
/// A named bundle of <see cref="WellKnownPermissions"/> (roadmap Epic 2E). Users are assigned roles; a role's
/// permissions are what its members are allowed to do once local auth enforces them (Phase 3). Persisted in the
/// <c>roles</c> collection (DbGateway is the source of truth, mirroring <c>zones</c>/<c>automations</c>).
///
/// <para>Three roles are seeded as <see cref="IsBuiltIn"/> (admin/resident/guest) so the model is usable out of
/// the box; built-in roles cannot be deleted (their permissions can still be edited). <b>No enforcement yet</b>
/// — nothing checks these in Phase 2; in dev everything stays open.</para>
/// </summary>
public class Role
{
    /// <summary>Stable role id (GUID string, or a slug like <c>admin</c> for built-ins). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the UI ("Administrator", "Resident", "Guest").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of what the role is for.</summary>
    public string? Description { get; set; }

    /// <summary>Seeded role that cannot be deleted (admin/resident/guest); its permissions remain editable.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Permissions granted to members of this role (values from <see cref="WellKnownPermissions"/>; open).</summary>
    public List<string> Permissions { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
