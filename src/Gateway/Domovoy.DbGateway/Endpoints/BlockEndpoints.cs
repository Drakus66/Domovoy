using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Devices;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Endpoints;

/// <summary>
/// CRUD for control-block instances (roadmap Epic 1H) in the <c>control_blocks</c> collection, mirroring
/// <see cref="AutomationEndpoints"/>. The AutomationService loads these over HTTP and ticks them; the
/// WebUI manages them via the ApiGateway proxy. On create the gateway derives the block's deterministic
/// <b>virtual device id</b> (<see cref="DeviceIdFactory"/>) so the runtime and UI agree on which
/// capability-device represents the block.
/// </summary>
public static class BlockEndpoints
{
    public const string Collection = "control_blocks";

    public static void MapBlockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/blocks").WithTags("ControlBlocks").WithOpenApi();

        group.MapGet("/", async (IMongoDatabase db) =>
            Results.Ok(await Blocks(db).Find(FilterDefinition<ControlBlock>.Empty).ToListAsync()));

        group.MapGet("/{id}", async (string id, IMongoDatabase db) =>
        {
            var block = await Blocks(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            return block is null ? Results.NotFound() : Results.Ok(block);
        });

        group.MapPost("/", async (ControlBlock block, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(block.Name)) return Results.BadRequest(new { error = "block name is required" });
            if (string.IsNullOrWhiteSpace(block.TypeId)) return Results.BadRequest(new { error = "block typeId is required" });

            block.Id = Guid.NewGuid().ToString();
            block.DeviceId = DeviceIdFactory.Derive("ControlBlock", block.Id).ToString();
            block.CreatedAt = block.UpdatedAt = DateTime.UtcNow;
            await Blocks(db).InsertOneAsync(block);
            return Results.Created($"/api/blocks/{block.Id}", block);
        });

        group.MapPut("/{id}", async (string id, ControlBlock block, IMongoDatabase db) =>
        {
            if (string.IsNullOrWhiteSpace(block.Name)) return Results.BadRequest(new { error = "block name is required" });

            var existing = await Blocks(db).Find(x => x.Id == id).FirstOrDefaultAsync();
            if (existing is null) return Results.NotFound();

            block.Id = id;
            block.DeviceId = existing.DeviceId; // stable virtual device id
            block.CreatedAt = existing.CreatedAt;
            block.UpdatedAt = DateTime.UtcNow;
            await Blocks(db).ReplaceOneAsync(x => x.Id == id, block);
            return Results.NoContent();
        });

        group.MapDelete("/{id}", async (string id, IMongoDatabase db) =>
        {
            var result = await Blocks(db).DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static IMongoCollection<ControlBlock> Blocks(IMongoDatabase db) =>
        db.GetCollection<ControlBlock>(Collection);
}
