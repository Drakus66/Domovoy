using Domovoy.Contracts.Security;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD endpoints for local roles (roadmap Epic 2E). Roles bundle permissions from the open
/// <see cref="WellKnownPermissions"/> vocabulary; users reference them (see <see cref="UsersEndpoints"/>).
/// DbGateway is the source of truth (<c>roles</c> collection), the ApiGateway forwards via RolesController.
/// Built-in roles (seeded by <see cref="Services.SecuritySeeder"/>) cannot be deleted; their permissions
/// remain editable. <b>No enforcement in Phase 2</b> — this is model-only.
/// </summary>
public static class RolesEndpoints
{
    public const string Collection = "roles";

    /// <summary>Request body for creating/updating a role (id is route/server assigned; IsBuiltIn is server-owned).</summary>
    public record RoleInput(string Name, string? Description, List<string>? Permissions);

    public static void MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
        {
            var roles = await Roles(db).Find(FilterDefinition<Role>.Empty).ToListAsync();
            return Results.Ok(roles);
        });

        // The permission vocabulary the role editor renders as checkboxes (open — plugins may add more).
        group.MapGet("/permissions", () => Results.Ok(WellKnownPermissions.All));

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var role = await Roles(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return role is null ? Results.NotFound() : Results.Ok(role);
        });

        group.MapPost("/", async (RoleInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return Results.BadRequest(new { error = "role name is required" });

            var now = DateTime.UtcNow;
            var role = new Role
            {
                Id = Guid.NewGuid().ToString(),
                Name = input.Name.Trim(),
                Description = input.Description,
                IsBuiltIn = false,
                Permissions = Clean(input.Permissions),
                CreatedAt = now,
                UpdatedAt = now,
            };
            await Roles(db).InsertOneAsync(role);
            return Results.Created($"/api/roles/{role.Id}", role);
        });

        // Update name/description/permissions. IsBuiltIn is never changed here (a built-in stays built-in).
        group.MapPut("/{id}", async (string id, RoleInput input, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                return Results.BadRequest(new { error = "role name is required" });

            var update = Builders<Role>.Update
                .Set(x => x.Name, input.Name.Trim())
                .Set(x => x.Description, input.Description)
                .Set(x => x.Permissions, Clean(input.Permissions))
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Roles(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var role = await Roles(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (role is null) return Results.NotFound();
            if (role.IsBuiltIn)
                return Results.BadRequest(new { error = "built-in roles cannot be deleted" });

            // Detach the deleted role from any user that held it, so no user points at a missing role.
            var users = db.GetCollection<User>(UsersEndpoints.Collection);
            await users.UpdateManyAsync(
                x => x.RoleIds.Contains(id),
                Builders<User>.Update.Pull(x => x.RoleIds, id).Set(x => x.UpdatedAt, DateTime.UtcNow));

            await Roles(db).DeleteOneAsync(x => x.Id == id);
            return Results.NoContent();
        });
    }

    // Drop blanks/dupes and keep a stable order — the stored permission set stays clean regardless of the client.
    private static List<string> Clean(List<string>? permissions) =>
        permissions is null
            ? new List<string>()
            : permissions.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();

    private static IMongoCollection<Role> Roles(IMongoDatabase db) => db.GetCollection<Role>(Collection);
}
