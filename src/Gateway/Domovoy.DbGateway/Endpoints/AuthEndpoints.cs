// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Domovoy.DbGateway.Services;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// Internal credential endpoints (mobile-app / remote-access track). DbGateway owns the credential store, so it
/// verifies passwords and resolves a user's roles into the union of their permissions. These are <b>in-cluster
/// only</b> — nginx exposes <c>/api/*</c> through the ApiGateway, not the DbGateway, and the ApiGateway calls
/// these over the private <c>db-gateway</c> network. The ApiGateway turns a successful verify into a signed JWT;
/// the plaintext password never leaves this boundary and the hash never crosses it.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth").WithOpenApi();

        // Verify a plaintext credential. Always returns 200 with {success:false} on any failure (unknown user,
        // wrong password, disabled, no password set) — no oracle that distinguishes "user exists" from "wrong
        // password", and no timing branch that leaks it (Verify runs the KDF only when a hash exists; a dummy
        // user still fails fast, which is acceptable for a single-household LAN-first system).
        group.MapPost("/verify-credentials", async (VerifyCredentialsRequest req, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.Ok(new VerifyCredentialsResponse(false, null));

            var username = req.Username.Trim();
            var user = await Users(db)
                .Find(x => x.Username != null && x.Username.ToLower() == username.ToLower())
                .FirstOrDefaultAsync();

            if (user is null || !user.Enabled || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Ok(new VerifyCredentialsResponse(false, null));

            var permissions = await ResolvePermissionsAsync(db, user.RoleIds);
            return Results.Ok(new VerifyCredentialsResponse(true, ToAuthUserInfo(user, permissions)));
        });

        // Re-resolve a user by id for token refresh: 404 if the user is gone or disabled, else the fresh
        // AuthUserInfo (permissions recomputed from current roles). This is what makes disabling a user or
        // changing a role take effect within one access-token lifetime.
        group.MapGet("/user-info/{id}", async (string id, IMongoDatabase db) =>
        {
            var user = await Users(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (user is null || !user.Enabled) return Results.NotFound();

            var permissions = await ResolvePermissionsAsync(db, user.RoleIds);
            return Results.Ok(ToAuthUserInfo(user, permissions));
        });
    }

    /// <summary>Union of the permissions of the given roles (system.admin implies everything, resolved client-side).</summary>
    internal static async Task<List<string>> ResolvePermissionsAsync(IMongoDatabase db, List<string> roleIds)
    {
        if (roleIds is null || roleIds.Count == 0) return new List<string>();

        var roles = await db.GetCollection<Role>(RolesEndpoints.Collection)
            .Find(r => roleIds.Contains(r.Id))
            .ToListAsync();

        return roles.SelectMany(r => r.Permissions)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToList();
    }

    internal static AuthUserInfo ToAuthUserInfo(User user, List<string> permissions) => new(
        user.Id,
        user.Username ?? string.Empty,
        user.DisplayName,
        user.Email,
        user.RoleIds,
        permissions);

    private static IMongoCollection<User> Users(IMongoDatabase db) => db.GetCollection<User>(UsersEndpoints.Collection);
}
