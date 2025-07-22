namespace Domovoy.DeviceService.Services;

using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Devices;

/// <summary>
/// Interface for the MQTT device adapter, responsible for sending commands to MQTT devices.
/// </summary>
public interface IMqttDeviceAdapter
{
    /// <summary>
    /// Sends a command to an MQTT device
    /// </summary>
    /// <param name="device">The MQTT device to send the command to</param>
    /// <param name="command">The command to send</param>
    /// <returns>A boolean indicating whether the command was sent successfully</returns>
    Task<bool> SendCommandAsync(MqttDevice device, DeviceCommand command);
    
    /// <summary>
    /// Sends a ping/heartbeat request to check if a device is available
    /// </summary>
    /// <param name="device">The MQTT device to check availability for</param>
    /// <returns>A boolean indicating whether the request was sent successfully</returns>
    Task<bool> SendHeartbeatRequestAsync(MqttDevice device);
}
