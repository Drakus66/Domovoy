namespace Domovoy.DeviceService.Services;

using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Enums;
using Domovoy.MessageBus;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adapter for sending commands to MQTT devices.
/// Handles translation of system commands to MQTT messages with appropriate topics and QoS levels.
/// </summary>
public class MqttDeviceAdapter : IMqttDeviceAdapter
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MqttDeviceAdapter> _logger;
    
    /// <summary>
    /// Initializes a new instance of the MqttDeviceAdapter class.
    /// </summary>
    /// <param name="messageBus">The message bus for MQTT communication</param>
    /// <param name="logger">Logger for the adapter</param>
    public MqttDeviceAdapter(IMessageBus messageBus, ILogger<MqttDeviceAdapter> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
        
        // Check if MQTT is enabled
        if (_messageBus is RabbitMqConnection rabbitMqConnection && !rabbitMqConnection.IsMqttEnabled)
        {
            _logger.LogWarning("MQTT is not enabled in the RabbitMqConnection. Commands may not be delivered correctly.");
        }
    }

    /// <summary>
    /// Sends a command to an MQTT device
    /// </summary>
    /// <param name="device">The MQTT device to send the command to</param>
    /// <param name="command">The command to send</param>
    /// <returns>A boolean indicating whether the command was sent successfully</returns>
    public async Task<bool> SendCommandAsync(MqttDevice device, DeviceCommand command)
    {
        if (device == null)
        {
            throw new ArgumentNullException(nameof(device));
        }
        
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }
        
        try
        {
            // Determine the appropriate topic for the command
            var topic = GetCommandTopic(device, command.CommandTypes);
            
            // Create the payload for the command
            var payload = CreateCommandPayload(device, command);
            
            // Log the command being sent
            _logger.LogInformation("Sending command {CommandType} to device {DeviceId} on topic {Topic}", 
                command.CommandTypes, device.Id, topic);
            
            // Send the command via MQTT
            await _messageBus.PublishAsync(
                "mqtt", // Exchange - using the mqtt exchange for all MQTT messages
                topic,  // Routing key - using the device's command topic
                payload);
            
            _logger.LogDebug("Command sent successfully to {DeviceId}", device.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending command to MQTT device {DeviceId}", device.Id);
            return false;
        }
    }
    
    /// <summary>
    /// Gets the appropriate command topic for the device and command type
    /// </summary>
    private static string GetCommandTopic(MqttDevice device, DeviceCommandTypes commandType)
    {
        // Use the device's command topic if available
        if (!string.IsNullOrEmpty(device.CommandTopic))
        {
            return device.CommandTopic;
        }
        
        // If device doesn't have a command topic, use a default format
        return MqttDevice.GetDefaultCommandTopic(device.Id.ToString());
    }
    
    /// <summary>
    /// Creates a JSON payload for the command based on its type and parameters
    /// </summary>
    private object CreateCommandPayload(MqttDevice device, DeviceCommand command)
    {
        // Create a payload object based on the command type
        switch (command.CommandTypes)
        {
            case DeviceCommandTypes.SetState:
                return new
                {
                    CommandType = "setState",
                    State = command.Parameters.ContainsKey("state") ? command.Parameters["state"] : "ON",
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = command.CorrelationId
                };
                
            case DeviceCommandTypes.GetState:
                return new
                {
                    CommandType = "getState",
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = command.CorrelationId
                };
                
            case DeviceCommandTypes.UpdateConfiguration:
                return new
                {
                    CommandType = "updateConfig",
                    Config = command.Parameters.ContainsKey("config") ? command.Parameters["config"] : new { },
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = command.CorrelationId
                };
                
            case DeviceCommandTypes.Identify:
                return new
                {
                    CommandType = "identify",
                    Duration = command.Parameters.ContainsKey("duration") ? command.Parameters["duration"] : 5,
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = command.CorrelationId
                };
                
            default:
                _logger.LogWarning("Unknown command type: {CommandType} for device {DeviceId}", 
                    command.CommandTypes, device.Id);
                    
                return new
                {
                    CommandType = command.CommandTypes.ToString(),
                    Parameters = command.Parameters,
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = command.CorrelationId
                };
        }
    }
    
    /// <summary>
    /// Sends a ping/heartbeat request to check if a device is available
    /// </summary>
    public async Task<bool> SendHeartbeatRequestAsync(MqttDevice device)
    {
        if (device == null)
        {
            throw new ArgumentNullException(nameof(device));
        }
        
        try
        {
            var topic = device.AvailabilityTopic;
            
            if (string.IsNullOrEmpty(topic))
            {
                topic = MqttDevice.GetDefaultAvailabilityTopic(device.Id.ToString());
            }
            
            // Add /ping suffix to the availability topic for heartbeat requests
            var pingTopic = $"{topic}/ping";
            
            var payload = new
            {
                Type = "heartbeat",
                Timestamp = DateTime.UtcNow,
                DeviceId = device.Id.ToString()
            };
            
            _logger.LogDebug("Sending heartbeat request to device {DeviceId} on topic {Topic}", device.Id, pingTopic);
            
            await _messageBus.PublishAsync(
                "mqtt",
                pingTopic,
                payload);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat request to device {DeviceId}", device.Id);
            return false;
        }
    }
}
