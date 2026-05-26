using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Models.DTOs;
using Domovoy.DbGateway.Repositories;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Domovoy.DbGateway.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices")
            .WithTags("Devices")
            .WithOpenApi();

        // Get all devices
        group.MapGet("/", async ([FromServices] IDeviceRepository repo, [FromServices] ILogger logger) =>
        {
            logger.LogInformation("GET all devices request received");
            var devices = await repo.GetAllAsync();
            logger.LogInformation("Returning {Count} devices", devices.Count());
            return Results.Ok(devices.Select(ToDto));
        });

        // Get device by ID
        group.MapGet("/{id}", async Task<Results<Ok<DeviceDto>, NotFound>> (string id, [FromServices] IDeviceRepository repo, [FromServices] ILogger logger) =>
        {
            logger.LogInformation("GET device request received for ID: {Id}", id);
            try
            {
                var device = await repo.GetByIdAsync(id);
                if (device == null) 
                {
                    logger.LogWarning("Device not found: {Id}", id);
                    return TypedResults.NotFound();
                }
                
                logger.LogInformation("Device found: {Id} - {Name}", device.DeviceId, device.Name);
                return TypedResults.Ok(ToDto(device));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting device {Id}", id);
                throw;
            }
        });

        // Get devices by location
        group.MapGet("/location/{location}", async (string location, [FromServices] IDeviceRepository repo) =>
        {
            var devices = await repo.GetByLocationAsync(location);
            return Results.Ok(devices.Select(ToDto));
        });

        // Get devices by type
        group.MapGet("/type/{type}", async (string type, [FromServices] IDeviceRepository repo) =>
        {
            var devices = await repo.GetByTypeAsync(type);
            return Results.Ok(devices.Select(ToDto));
        });

        // Create device
        group.MapPost("/", async Task<Results<Created<DeviceDto>, BadRequest>> ([FromBody] CreateDeviceDto deviceDto, [FromServices] IDeviceRepository repo) =>
        {
            var device = new Device
            {
                DeviceId = Guid.NewGuid().ToString(),
                Name = deviceDto.Name,
                Type = deviceDto.Type,
                LocationId = deviceDto.Location,
                Configuration = deviceDto.Configuration ?? new(),
                Status = "Offline",
                LastSeen = DateTime.UtcNow
            };

            await repo.CreateAsync(device);
            return TypedResults.Created($"/api/devices/{device.DeviceId}", ToDto(device));
        });

        // Update device
        group.MapPut("/{id}", async Task<Results<Ok<DeviceDto>, NotFound>> (string id, [FromBody] UpdateDeviceDto deviceDto, [FromServices] IDeviceRepository repo) =>
        {
            var existingDevice = await repo.GetByIdAsync(id);
            if (existingDevice == null) return TypedResults.NotFound();

            existingDevice.Name = deviceDto.Name;
            existingDevice.Type = deviceDto.Type;
            existingDevice.LocationId = deviceDto.Location;
            if (deviceDto.Configuration != null)
                existingDevice.Configuration = deviceDto.Configuration;

            await repo.UpdateAsync(id, existingDevice);
            return TypedResults.Ok(ToDto(existingDevice));
        });

        // Update device state
        group.MapPatch("/{id}/state",
            async Task<Results<Ok<DeviceDto>, NotFound>> (string id, [FromBody] UpdateDeviceStateDto stateDto, [FromServices] IDeviceRepository repo) =>
            {
                var existingDevice = await repo.GetByIdAsync(id);
                if (existingDevice == null) return TypedResults.NotFound();

                await repo.UpdateStateAsync(id, stateDto.State);
                return TypedResults.Ok(ToDto(existingDevice));
            });

        // Update device online status
        group.MapPatch("/{id}/online", async Task<Results<Ok<DeviceDto>, NotFound>> (string id, bool isOnline, [FromServices] IDeviceRepository repo) =>
        {
            var existingDevice = await repo.GetByIdAsync(id);
            if (existingDevice == null) return TypedResults.NotFound();

            await repo.UpdateOnlineStatusAsync(id, isOnline);
            existingDevice.IsOnline = isOnline;
            return TypedResults.Ok(ToDto(existingDevice));
        });

        // Delete device
        group.MapDelete("/{id}", async Task<Results<Ok, NotFound>> (string id, [FromServices] IDeviceRepository repo) =>
        {
            var device = await repo.GetByIdAsync(id);
            if (device == null) return TypedResults.NotFound();

            await repo.DeleteAsync(id);
            return TypedResults.Ok();
        });
    }

    private static DeviceDto ToDto(Device device) => new(
        device.DeviceId,
        device.Name,
        device.Type,
        device.LocationId,
        device.Status,
        device.IsOnline,
        device.LastSeen,
        device.Configuration
    );
}
