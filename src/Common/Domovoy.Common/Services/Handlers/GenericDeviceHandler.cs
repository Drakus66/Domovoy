using Domovoy.Common.Models;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Enums;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Events;

using Microsoft.Extensions.Logging;

namespace Domovoy.Common.Services.Handlers;

/// <summary>
/// Handler for Generic device types (switches, relays, plugs, etc.).
/// Provides full command processing: SetState, GetState, UpdateConfiguration.
/// </summary>
public class GenericDeviceHandler : IDeviceTypeHandler
{
    public GlobalEntityTypes HandledType => GlobalEntityTypes.Generic;

    public BaseEntity CreateFromDiscovery(DeviceDiscoveredEvent discoveryEvent)
    {
        var device = new Device
        {
            Id = discoveryEvent.DeviceId,
            Name = discoveryEvent.Name,
            State = discoveryEvent.State,
            LastUpdated = DateTime.UtcNow
        };

        if (discoveryEvent.Metadata != null)
        {
            foreach (var kvp in discoveryEvent.Metadata)
            {
                device.Metadata[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        }
        
        // Ensure Adapter Source is saved
        if (!string.IsNullOrEmpty(discoveryEvent.Source))
        {
            device.Metadata["AdapterSource"] = discoveryEvent.Source;
        }

        return device;
    }

    public async Task HandleCommand(BaseEntity device, DeviceCommand command, ILogger logger)
    {
        if (device is not Device genericDevice)
        {
            logger.LogWarning("GenericDeviceHandler received a non-Device entity: {Type}", device.GetType().Name);
            return;
        }

        switch (command.CommandTypes)
        {
            case DeviceCommandTypes.SetState:
                await HandleSetState(genericDevice, command, logger);
                break;

            case DeviceCommandTypes.GetState:
                logger.LogDebug("Getting state for device {DeviceId}", device.Id);
                break;

            case DeviceCommandTypes.UpdateConfiguration:
                HandleUpdateConfiguration(genericDevice, command, logger);
                break;

            default:
                logger.LogWarning("Unknown command type: {CommandType} for device {DeviceId}",
                    command.CommandTypes, device.Id);
                break;
        }
    }

    public Task HandleEvent(BaseEntity device, BaseEvent @event, ILogger logger)
    {
        // TODO: Implement generic device event handling
        return Task.CompletedTask;
    }

    public Task UpdateState(BaseEntity device, Dictionary<string, object> state)
    {
        if (device is not Device genericDevice) return Task.CompletedTask;

        foreach (var param in state)
        {
            genericDevice.State[param.Key] = param.Value;
        }

        genericDevice.LastUpdated = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task UpdateOnlineStatus(BaseEntity device, bool isOnline)
    {
        if (isOnline)
        {
            device.LastUpdated = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    private static Task HandleSetState(Device device, DeviceCommand command, ILogger logger)
    {
        logger.LogDebug("Setting state for device {DeviceId}", device.Id);

        foreach (var param in command.Parameters)
        {
            device.State[param.Key] = param.Value;
        }

        device.LastUpdated = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    private static void HandleUpdateConfiguration(Device device, DeviceCommand command, ILogger logger)
    {
        logger.LogDebug("Updating configuration for device {DeviceId}", device.Id);

        if (command.Parameters.TryGetValue("name", out var nameValue) && nameValue != null)
        {
            var newName = nameValue.ToString() ?? string.Empty;
            if (device.Name != newName)
            {
                device.Name = newName;
            }
        }

        if (command.Parameters.TryGetValue("location", out var locationValue) && locationValue != null)
        {
            var newLocation = locationValue.ToString() ?? string.Empty;
            if (newLocation != device.LocationId.ToString())
            {
                device.LocationId = new Guid(newLocation);
            }
        }

        device.LastUpdated = DateTime.UtcNow;
    }
}
