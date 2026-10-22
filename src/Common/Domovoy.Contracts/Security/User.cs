// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json.Serialization;

namespace Domovoy.Contracts.Security;

/// <summary>
/// A local household member (roadmap Epic 2E — roles model). A user is an identity plus assigned
/// <see cref="RoleIds"/>. Authentication was added on top of this model without a data migration (the
/// mobile-app / remote-access track): <see cref="Username"/> + <see cref="PasswordHash"/> are the login
/// credential; they stay <b>optional</b> so pre-existing users (created before auth) simply cannot log in
/// until a password is set. Persisted in the <c>users</c> collection.
/// </summary>
public class User
{
    /// <summary>Stable user id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Login handle (unique, case-insensitive). Null for identity-only users that cannot log in yet.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// PBKDF2 password hash in the self-describing <c>v1.{iterations}.{saltB64}.{keyB64}</c> format
    /// (see the gateway's PasswordHasher). <see cref="JsonIgnoreAttribute"/> keeps it out of every JSON API
    /// response — Mongo uses its own serializer, so the field is still persisted. Null = no password set.
    /// </summary>
    [JsonIgnore]
    public string? PasswordHash { get; set; }

    /// <summary>Display name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional email — a contact/identity hint, not a login credential.</summary>
    public string? Email { get; set; }

    /// <summary>Roles assigned to this user (ids into the <c>roles</c> collection); the union of their permissions applies.</summary>
    public List<string> RoleIds { get; set; } = new();

    /// <summary>Operator switch — a disabled user keeps their record but would be denied once enforcement exists.</summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
