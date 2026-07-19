// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Domovoy.DbGateway.Services;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD endpoints for local users (roadmap Epic 2E). A user is an identity plus assigned role ids. Authentication
/// (mobile-app / remote-access track) added an optional login <see cref="User.Username"/> + password; the password
/// is set through the dedicated <c>/{id}/password</c> endpoint, never through create/update (so a hash can't be
/// posted directly). DbGateway owns the <c>users</c> collection; the ApiGateway forwards via UsersController.
/// </summary>
public static class UsersEndpoints
{
    public const string Collection = "users";

    /// <summary>Request body for creating/updating a user (id is route/server assigned; password is set separately).</summary>
    public record UserInput(string DisplayName, string? Email, string? Username, List<string>? RoleIds, bool Enabled = true);

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var users = await Users(db).Find(FilterDefinition<User>.Empty).ToListAsync();
            return Results.Ok(users);
        });

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var user = await Users(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        group.MapPost("/", async (UserInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                return Results.BadRequest(new { error = "user display name is required" });

            var username = NormalizeUsername(input.Username);
            if (username is not null && await UsernameTakenAsync(db, username, excludeId: null))
                return Results.Conflict(new { error = "username is already taken" });

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                DisplayName = input.DisplayName.Trim(),
                Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
                RoleIds = CleanRoles(input.RoleIds),
                Enabled = input.Enabled,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await Users(db).InsertOneAsync(user);
            return Results.Created($"/api/users/{user.Id}", user);
        });

        group.MapPut("/{id}", async (string id, UserInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.DisplayName))
                return Results.BadRequest(new { error = "user display name is required" });

            var username = NormalizeUsername(input.Username);
            if (username is not null && await UsernameTakenAsync(db, username, excludeId: id))
                return Results.Conflict(new { error = "username is already taken" });

            var update = Builders<User>.Update
                .Set(x => x.DisplayName, input.DisplayName.Trim())
                .Set(x => x.Email, string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim())
                .Set(x => x.Username, username)
                .Set(x => x.RoleIds, CleanRoles(input.RoleIds))
                .Set(x => x.Enabled, input.Enabled)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Users(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Set or change a user's password. Self-service change requires the current password; an admin reset
        // omits it (the ApiGateway gates the reset path on users.manage before forwarding here).
        group.MapPut("/{id}/password", async (string id, SetPasswordRequest req, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return Results.BadRequest(new { error = "password must be at least 8 characters" });

            var user = await Users(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (user is null) return Results.NotFound();

            // If a current password was supplied, it must match (self-service change).
            if (req.CurrentPassword is not null && !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new { error = "current password is incorrect" });

            var update = Builders<User>.Update
                .Set(x => x.PasswordHash, PasswordHasher.Hash(req.NewPassword))
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            await Users(db).UpdateOneAsync(x => x.Id == id, update);
            return Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Users(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static string? NormalizeUsername(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : username.Trim();

    private static async Task<bool> UsernameTakenAsync(IMongoDatabase db, string username, string? excludeId)
    {
        var lower = username.ToLower();
        var match = await Users(db)
            .Find(x => x.Username != null && x.Username.ToLower() == lower)
            .FirstOrDefaultAsync();
        return match is not null && match.Id != excludeId;
    }

    private static List<string> CleanRoles(List<string>? roleIds) =>
        roleIds is null
            ? new List<string>()
            : roleIds.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();

    private static IMongoCollection<User> Users(IMongoDatabase db) => db.GetCollection<User>(Collection);
}
