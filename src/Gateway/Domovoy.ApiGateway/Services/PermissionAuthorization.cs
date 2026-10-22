// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;

namespace Domovoy.ApiGateway.Services;

/// <summary>Whether request-gating is switched on. Injected into the permission handler so policies pass through
/// untouched when auth is disabled (dev / integration tests run open, exactly as before this track).</summary>
public sealed class AuthEnforcementOptions
{
    public bool Enabled { get; init; }
}

/// <summary>A policy requirement that the principal holds a specific permission (or the super-permission).</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;
    public string Permission { get; }
}

/// <summary>
/// Grants a <see cref="PermissionRequirement"/> when the authenticated principal carries a matching
/// <see cref="AuthConstants.PermissionClaim"/> — or the <see cref="WellKnownPermissions.SystemAdmin"/> super-claim,
/// which implies every permission. When enforcement is <b>disabled</b> the requirement auto-succeeds so the whole
/// API stays open in dev without any per-endpoint conditionals.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly bool _enabled;

    public PermissionAuthorizationHandler(AuthEnforcementOptions options) => _enabled = options.Enabled;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!_enabled)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var hasPermission = context.User.HasClaim(AuthConstants.PermissionClaim, requirement.Permission)
                            || context.User.HasClaim(AuthConstants.PermissionClaim, WellKnownPermissions.SystemAdmin);
        if (hasPermission)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public static class PermissionPolicies
{
    /// <summary>
    /// Register one authorization policy per <see cref="WellKnownPermissions"/> value, named exactly as the
    /// permission string (so controllers annotate with <c>[Authorize(Policy = WellKnownPermissions.DevicesControl)]</c>).
    /// Each policy holds only a <see cref="PermissionRequirement"/> — deliberately NOT RequireAuthenticatedUser —
    /// so when enforcement is off the handler passes it through for anonymous requests too. Authenticated-user
    /// gating for every other endpoint is the fallback policy, added only when enforcement is on.
    /// </summary>
    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in WellKnownPermissions.All)
        {
            options.AddPolicy(permission, policy => policy.AddRequirements(new PermissionRequirement(permission)));
        }
    }
}
