// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Security;

/// <summary>
/// A local household member (roadmap Epic 2E — roles model). A user is just an identity plus assigned
/// <see cref="RoleIds"/>; there is deliberately <b>no password / login / session</b> here — authentication and
/// request-gating are Phase 3 (before smart locks). This epic only records who exists and what roles they hold,
/// so enforcement can be switched on later without a data migration. Persisted in the <c>users</c> collection.
/// </summary>
public class User
{
    /// <summary>Stable user id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional email — a contact/identity hint, not a login credential in Phase 2.</summary>
    public string? Email { get; set; }

    /// <summary>Roles assigned to this user (ids into the <c>roles</c> collection); the union of their permissions applies.</summary>
    public List<string> RoleIds { get; set; } = new();

    /// <summary>Operator switch — a disabled user keeps their record but would be denied once enforcement exists.</summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
