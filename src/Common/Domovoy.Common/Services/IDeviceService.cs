using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;

namespace Domovoy.Common.Services;

/// <summary>
/// Defines the core functionality for device-related services in the Domovoy system.
/// This interface provides a contract for handling device commands, events, and state management.
/// </summary>
public interface IDeviceService
{
    /// <summary>
    /// Handles a device command received from the message bus or other sources.
    /// </summary>
    /// <param name="command">The device command to handle.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleDeviceCommand(BaseCommand command);

    /// <summary>
    /// Handles a device event received from the message bus or other sources.
    /// </summary>
    /// <param name="event">The device event to handle.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleDeviceEvent(BaseEvent @event);

    /// <summary>
    /// Updates the state of a device with the specified state dictionary.
    /// </summary>
    /// <param name="deviceId">The unique identifier of the device.</param>
    /// <param name="state">A dictionary containing the device's state properties.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateDeviceState(string deviceId, Dictionary<string, object> state);

    /// <summary>
    /// Updates the online status of a device.
    /// </summary>
    /// <param name="deviceId">The unique identifier of the device.</param>
    /// <param name="isOnline">A boolean indicating whether the device is online.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline);
}