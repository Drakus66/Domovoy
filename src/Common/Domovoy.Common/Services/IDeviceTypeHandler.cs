using Domovoy.Common.Models;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Models.Enums.EntityTypes;

using Microsoft.Extensions.Logging;

namespace Domovoy.Common.Services;

/// <summary>
/// Defines a strategy for handling a specific type of device.
/// Each handler encapsulates device-type-specific logic for creation, command processing,
/// event handling, and state management.
/// </summary>
public interface IDeviceTypeHandler
{
    /// <summary>
    /// The device type this handler is responsible for.
    /// </summary>
    GlobalEntityTypes HandledType { get; }

    /// <summary>
    /// Creates a device entity from a discovery event.
    /// </summary>
    /// <param name="discoveryEvent">The discovery event containing device metadata.</param>
    /// <returns>A new device entity of the appropriate type.</returns>
    BaseEntity CreateFromDiscovery(DeviceDiscoveredEvent discoveryEvent);

    /// <summary>
    /// Handles a command for a device of this type.
    /// </summary>
    /// <param name="device">The target device entity.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleCommand(BaseEntity device, DeviceCommand command, ILogger logger);

    /// <summary>
    /// Handles an event for a device of this type.
    /// </summary>
    /// <param name="device">The target device entity.</param>
    /// <param name="event">The event to process.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleEvent(BaseEntity device, BaseEvent @event, ILogger logger);

    /// <summary>
    /// Applies a state update to the device from a raw dictionary.
    /// Handles type-specific parsing (e.g., LightState, SensorState).
    /// </summary>
    /// <param name="device">The target device entity.</param>
    /// <param name="state">Raw state dictionary to apply.</param>
    Task UpdateState(BaseEntity device, Dictionary<string, object> state);

    /// <summary>
    /// Updates the online status of a device.
    /// </summary>
    /// <param name="device">The target device entity.</param>
    /// <param name="isOnline">Whether the device is online.</param>
    Task UpdateOnlineStatus(BaseEntity device, bool isOnline);
}
