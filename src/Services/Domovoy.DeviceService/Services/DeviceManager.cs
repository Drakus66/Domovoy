namespace Domovoy.DeviceService.Services;

using Domovoy.Common.Models;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Enums;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Services;
using Domovoy.MessageBus;
using Domovoy.Common.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

/// <summary>
/// Manages device operations and state in the Domovoy system.
/// This service handles device registration, state updates, and command processing
/// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
/// </summary>
public class DeviceManager : BaseService
{
    private readonly Dictionary<Guid, Device> _devices = new();
    private readonly Dictionary<Guid, DateTime> _deviceLastModified = new();
    private readonly IMqttDeviceAdapter _mqttDeviceAdapter;

    /// <summary>
    /// Initializes a new instance of the DeviceManager class.
    /// </summary>
    /// <param name="messageBus">The message bus for pubsub operations</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
    /// <param name="logger">Logger for the service</param>
    /// <param name="options">Service configuration options</param>
    /// <param name="mqttDeviceAdapter">Communication adapter for MQTT devices</param>
    public DeviceManager(
        IMessageBus messageBus,
        IHttpClientFactory httpClientFactory,
        ILogger<DeviceManager> logger,
        IOptions<BaseServiceOptions> options,
        IMqttDeviceAdapter mqttDeviceAdapter)
        : base(messageBus, httpClientFactory, logger, options)
    {
        _mqttDeviceAdapter = mqttDeviceAdapter;
    }

    /// <summary>
    /// Handles device commands received from the message bus
    /// </summary>
    public override async Task HandleDeviceCommand(BaseCommand baseCommand)
    {
        if (baseCommand is not DeviceCommand command)
        {
            Logger.LogWarning("Received invalid command type: {CommandType}", baseCommand.GetType().Name);
            return;
        }

        var deviceId = command.DeviceId;
        Logger.LogInformation("Handling command {CommandType} for device {DeviceId}", command.CommandTypes, deviceId);
        
        // Load device if it's not in the cache
        if (!_devices.ContainsKey(deviceId))
        {
            await LoadDeviceFromDb(deviceId.ToString());
        }

        // Check if device exists after loading attempt
        if (!_devices.ContainsKey(deviceId))
        {
            Logger.LogWarning("Device not found: {DeviceId}", deviceId);
            
            // Publish error response event
            await PublishEvent(
                Options.EventExchange, 
                new DeviceEvent
                {
                    DeviceId = command.DeviceId,
                    EventType = DeviceEventTypes.CommandResponse,
                    Data = new Dictionary<string, object> { { "error", "Device not found" } },
                    CorrelationId = command.CorrelationId,
                    Source = GetType().Name,
                    Timestamp = DateTime.UtcNow
                }, 
                "event.device.error"
            );
            return;
        }

        var device = _devices[deviceId];

        try
        {
            // Check if this is an MQTT device
            if (device is MqttDevice mqttDevice)
            {
                // Handle command via MQTT adapter
                Logger.LogInformation("Sending command to MQTT device {DeviceId}", deviceId);
                var success = await _mqttDeviceAdapter.SendCommandAsync(mqttDevice, command);
                
                if (!success)
                {
                    Logger.LogWarning("Failed to send command to MQTT device {DeviceId}", deviceId);
                    // Publish error response event
                    await PublishEvent(
                        Options.EventExchange, 
                        new DeviceEvent
                        {
                            DeviceId = command.DeviceId,
                            EventType = DeviceEventTypes.CommandResponse,
                            Data = new Dictionary<string, object> { { "error", "Failed to send command to MQTT device" } },
                            CorrelationId = command.CorrelationId,
                            Source = GetType().Name,
                            Timestamp = DateTime.UtcNow
                        }, 
                        "event.device.error"
                    );
                }
            }
            else
            {
                // Handle command for non-MQTT devices using existing logic
                switch (command.CommandTypes)
                {
                    case DeviceCommandTypes.SetState:
                        await HandleSetStateCommand(device, command);
                        break;

                    case DeviceCommandTypes.GetState:
                        await HandleGetStateCommand(device, command);
                        break;

                    case DeviceCommandTypes.UpdateConfiguration:
                        await HandleUpdateConfigurationCommand(device, command);
                        break;
                        
                    default:
                        Logger.LogWarning("Unknown command type: {CommandType} for device {DeviceId}", 
                            command.CommandTypes, deviceId);
                        break;
                }
            }
            
            // Mark device as modified so it will be saved in the next update cycle
            _deviceLastModified[deviceId] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing command {CommandType} for device {DeviceId}", 
                command.CommandTypes, deviceId);
                
            // Publish error event
            await PublishEvent(
                Options.EventExchange, 
                new DeviceEvent
                {
                    DeviceId = command.DeviceId,
                    EventType = DeviceEventTypes.CommandResponse,
                    Data = new Dictionary<string, object> { { "error", ex.Message } },
                    CorrelationId = command.CorrelationId,
                    Source = GetType().Name,
                    Timestamp = DateTime.UtcNow
                }, 
                "event.device.error"
            );
        }
    }

    public override Task HandleDeviceEvent(BaseEvent @event)
    {
        // Implement if needed
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Checks if MQTT devices are still available using heartbeat mechanism
    /// </summary>
    public async Task CheckMqttDevicesAvailability()
    {
        try
        {
            // Get all MQTT devices
            var mqttDevices = _devices.Values
                .OfType<MqttDevice>()
                .ToList();
            
            if (!mqttDevices.Any())
            {
                return;
            }
            
            Logger.LogInformation("Checking availability for {Count} MQTT devices", mqttDevices.Count);
            
            foreach (var device in mqttDevices)
            {
                // Check when the last heartbeat was received
                var timeSinceLastHeartbeat = DateTime.UtcNow - device.LastHeartbeat;
                var heartbeatIntervalMs = device.HeartbeatInterval * 1000; // Convert to milliseconds
                
                // If it's been longer than the heartbeat interval plus a buffer, send a ping
                if (timeSinceLastHeartbeat.TotalMilliseconds > heartbeatIntervalMs * 1.5)
                {
                    Logger.LogDebug("Sending heartbeat request to device {DeviceId}", device.Id);
                    await _mqttDeviceAdapter.SendHeartbeatRequestAsync(device);
                }
                
                // If it's been longer than the max missed heartbeats threshold, mark as offline
                if (timeSinceLastHeartbeat.TotalMilliseconds > heartbeatIntervalMs * device.MaxMissedHeartbeats)
                {
                    // Mark device as offline if it was previously online
                    if (device.IsOnline)
                    {
                        device.LastUpdated = DateTime.UtcNow;
                        _deviceLastModified[device.Id] = DateTime.UtcNow;
                        
                        // Publish device unavailable event
                        await PublishEvent(
                            Options.EventExchange,
                            new DeviceEvent
                            {
                                DeviceId = device.Id,
                                EventType = DeviceEventTypes.DeviceUnavailable,
                                Data = new Dictionary<string, object> { { "device", device } },
                                Source = GetType().Name,
                                Timestamp = DateTime.UtcNow
                            },
                            "event.device.unavailable"
                        );
                        
                        Logger.LogWarning("Device {DeviceId} marked as offline due to missed heartbeats", device.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking MQTT devices availability");
        }
    }

    public override void SetupMessageBusSubscriptions()
    {
        SubscribeToCommands<DeviceCommand>(
            MessageBusConfiguration.DeviceCommandsQueue, 
            HandleDeviceCommand);
            
        SubscribeToEvents<DeviceEvent>(
            MessageBusConfiguration.DeviceEventsQueue, 
            HandleDeviceEvent);
    }

    /// <summary>
    /// Loads a device from the database via DbGateway
    /// </summary>
    private async Task LoadDeviceFromDb(string deviceId)
    {
        try
        {
            Logger.LogDebug("Loading device {DeviceId} from database", deviceId);
            var device = await LoadFromDb<Device>(deviceId, "devices");
            
            if (device != null)
            {
                _devices[new Guid(deviceId)] = device;
                Logger.LogInformation("Successfully loaded device {DeviceId} ({DeviceName}) from database", 
                    deviceId, device.Name);
            }
            else
            {
                Logger.LogWarning("Device {DeviceId} not found in database", deviceId);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading device {DeviceId} from database", deviceId);
        }
    }
    
    /// <summary>
    /// Handles the SetState command for a device
    /// </summary>
    private async Task HandleSetStateCommand(Device device, DeviceCommand command)
    {
        Logger.LogDebug("Setting state for device {DeviceId}", device.Id);
        
        // Update device state with command parameters
        foreach (var param in command.Parameters)
        {
            device.State[param.Key] = param.Value;
        }
        
        device.LastUpdated = DateTime.UtcNow;
        
        // Update device state in the database via BaseService method
        await UpdateDeviceState(device.Id.ToString(), device.State);
        
        // Publish state changed event
        await PublishStateChanged(device, command.CorrelationId);
    }
    
    /// <summary>
    /// Handles the GetState command for a device
    /// </summary>
    private async Task HandleGetStateCommand(Device device, DeviceCommand command)
    {
        Logger.LogDebug("Getting state for device {DeviceId}", device.Id);
        
        // Publish the current state as a response
        await PublishEvent(
            Options.EventExchange, 
            new DeviceEvent
            {
                DeviceId = device.Id,
                EventType = DeviceEventTypes.CommandResponse,
                Data = new Dictionary<string, object> { { "state", device.State } },
                CorrelationId = command.CorrelationId,
                Source = GetType().Name,
                Timestamp = DateTime.UtcNow
            }, 
            "event.device.state.response"
        );
    }
    
    /// <summary>
    /// Handles the UpdateConfiguration command for a device
    /// </summary>
    private async Task HandleUpdateConfigurationCommand(Device device, DeviceCommand command)
    {
        Logger.LogDebug("Updating configuration for device {DeviceId}", device.Id);
        
        bool configChanged = false;
        
        // Update device name if provided
        if (command.Parameters.TryGetValue("name", out var nameValue) && nameValue != null)
        {
            var newName = nameValue.ToString() ?? string.Empty;
            if (device.Name != newName)
            {
                device.Name = newName;
                configChanged = true;
            }
        }
        
        // Update other configuration parameters as needed
        if (command.Parameters.TryGetValue("location", out var locationValue) && locationValue != null)
        {
            var newLocation = locationValue.ToString() ?? string.Empty;
            if (newLocation != device.LocationId.ToString())
            {
                // In a real implementation, we would verify that the location exists
                device.LocationId = new Guid(newLocation);
                configChanged = true;
            }
        }
        
        // Update device configuration if changed
        if (configChanged)
        {
            device.LastUpdated = DateTime.UtcNow;
            
            // Save updated device to database
            await SaveToDb(device, "devices");
            
            // Publish configuration updated event
            await PublishEvent(
                Options.EventExchange, 
                new DeviceEvent
                {
                    DeviceId = device.Id,
                    EventType = DeviceEventTypes.ConfigurationUpdated,
                    Data = new Dictionary<string, object> { { "device", device } },
                    CorrelationId = command.CorrelationId,
                    Source = GetType().Name,
                    Timestamp = DateTime.UtcNow
                }, 
                "event.device.updated"
            );
        }
    }

    /// <summary>
    /// Saves device states to the database in batches
    /// </summary>
    protected override void SaveStates(object? state)
    {
        try
        {
            // Check MQTT devices availability
            _ = CheckMqttDevicesAvailability();
            
            // Get all devices modified since last save
            var modifiedDevices = _deviceLastModified
                .Where(kvp => kvp.Value > DateTime.UtcNow.AddMinutes(-5)) // Only devices modified in the last 5 minutesKey(kvp.Key) && 
                .Select(kvp => kvp.Key)
                .ToList();
            
            if (modifiedDevices.Count == 0)
            {
                return;
            }
            
            Logger.LogInformation("Saving {Count} modified devices to database", modifiedDevices.Count);
            
            foreach (var deviceId in modifiedDevices)
            {
                // Use Task.Run to avoid blocking but still handle exceptions
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await SaveToDb(_devices[deviceId], "devices");
                        Logger.LogDebug("Successfully saved device {DeviceId} to database", deviceId);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error saving device {DeviceId} to database", deviceId);
                    }
                });
            }
            
            // Clean up old entries in the last modified dictionary
            var oldEntries = _deviceLastModified.Where(kvp => 
                DateTime.UtcNow.Subtract(kvp.Value).TotalSeconds > Options.StateUpdateIntervalSeconds * 3)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var key in oldEntries)
            {
                _deviceLastModified.Remove(key);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in SaveStates");
        }
    }

    /// <summary>
    /// Publishes a state changed event for a device
    /// </summary>
    private async Task PublishStateChanged(Device device, Guid? correlationId = null)
    {
        try
        {
            // Publish state changed event to notify other services
            await PublishEvent(
                Options.EventExchange, 
                new DeviceEvent
                {
                    DeviceId = device.Id,
                    EventType = DeviceEventTypes.StateChanged,
                    Data = new Dictionary<string, object>
                    {
                        { "state", device.State },
                        { "lastUpdated", device.LastUpdated }
                    },
                    CorrelationId = correlationId ?? Guid.NewGuid(),
                    Source = GetType().Name,
                    Timestamp = DateTime.UtcNow
                }, 
                "event.device.state.changed"
            );
            
            Logger.LogDebug("Published state changed event for device {DeviceId}", device.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing state changed for device {DeviceId}", device.Id);
            throw;
        }
    }
    
    /// <summary>
    /// Gets a device by its ID, loading it from the database if necessary
    /// </summary>
    public async Task<Device?> GetDeviceAsync(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            throw new ArgumentNullException(nameof(deviceId));
        }
            
        // Check if device is already in memory
        if (_devices.TryGetValue(new Guid(deviceId), out var device))
        {
            return device;
        }
            
        // Load device from database
        await LoadDeviceFromDb(deviceId);
            
        // Check if device was loaded successfully
        return _devices.TryGetValue(new Guid(deviceId), out device) ? device : null;
    }
    
    /// <summary>
    /// Registers a new device in the system
    /// </summary>
    public async Task<Device> RegisterDeviceAsync(Device newDevice)
    {
        if (newDevice == null)
        {
            throw new ArgumentNullException(nameof(newDevice));
        }
            
        // Make sure the device has required properties
        if (string.IsNullOrEmpty(newDevice.Name))
        {
            throw new ArgumentException("Device name cannot be empty");
        }
            
        // Set last updated time
        newDevice.LastUpdated = DateTime.UtcNow;
            
        // Save device to database
        await SaveToDb(newDevice, "devices");
            
        // Add to local cache
        _devices[newDevice.Id] = newDevice;
        _deviceLastModified[newDevice.Id] = DateTime.UtcNow;
            
        // Publish device registered event
        await PublishEvent(
            Options.EventExchange,
            new DeviceEvent
            {
                DeviceId = newDevice.Id,
                EventType = DeviceEventTypes.DeviceRegistered,
                Data = new Dictionary<string, object> { { "device", newDevice } },
                Source = GetType().Name,
                Timestamp = DateTime.UtcNow
            },
            "event.device.registered"
        );
            
        Logger.LogInformation("Registered new device: {DeviceName} ({DeviceId})", newDevice.Name, newDevice.Id);
            
        return newDevice;
    }
}
