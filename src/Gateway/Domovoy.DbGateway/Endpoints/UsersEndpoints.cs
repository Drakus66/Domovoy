using Domovoy.Contracts.Security;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD endpoints for local users (roadmap Epic 2E). A user is an identity plus assigned role ids — there is
/// no password/login here (authentication is Phase 3). DbGateway owns the <c>users</c> collection; the ApiGateway
/// forwards via UsersController. <b>No enforcement in Phase 2</b> — creating/deleting users blocks nothing in dev.
/// </summary>
public static class UsersEndpoints
{
    public const string Collection = "users";

    /// <summary>Request body for creating/updating a user (id is route/server assigned).</summary>
    public record UserInput(string DisplayName, string? Email, List<string>? RoleIds, bool Enabled = true);

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

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
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

            var update = Builders<User>.Update
                .Set(x => x.DisplayName, input.DisplayName.Trim())
                .Set(x => x.Email, string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim())
                .Set(x => x.RoleIds, CleanRoles(input.RoleIds))
                .Set(x => x.Enabled, input.Enabled)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await Users(db).UpdateOneAsync(x => x.Id == id, update);
            return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Users(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static List<string> CleanRoles(List<string>? roleIds) =>
        roleIds is null
            ? new List<string>()
            : roleIds.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();

    private static IMongoCollection<User> Users(IMongoDatabase db) => db.GetCollection<User>(Collection);
}
