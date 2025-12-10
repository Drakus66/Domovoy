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
    /// Handles device discovery events from DiscoveryService
    /// Only processes Generic device types (switches, relays, etc.)
    /// </summary>
    public async Task HandleDeviceDiscovered(DeviceDiscoveredEvent discoveryEvent)
    {
        // Filter: Only handle Generic devices (Light and Sensor are handled by their respective managers)
        if (discoveryEvent.DeviceType != Common.Models.Enums.EntityTypes.GlobalEntityTypes.Generic)
        {
            Logger.LogDebug("Ignoring {DeviceType} device, not a Generic device", discoveryEvent.DeviceType);
            return;
        }

        Logger.LogInformation("Received device discovery: {DeviceName} ({DeviceId})",
            discoveryEvent.Name, discoveryEvent.DeviceId);

        // Check if device already exists
        if (!_devices.ContainsKey(discoveryEvent.DeviceId))
        {
            await LoadDeviceFromDb(discoveryEvent.DeviceId.ToString());
        }

        if (_devices.TryGetValue(discoveryEvent.DeviceId, out var existingDevice))
        {
            Logger.LogInformation("Device already registered: {DeviceId}", discoveryEvent.DeviceId);
            existingDevice.LastUpdated = DateTime.UtcNow;
            await SaveToDb(existingDevice, "devices");
            return;
        }

        // Extract MQTT topics from metadata
        var commandTopic = discoveryEvent.Metadata.ContainsKey("command_topic")
            ? discoveryEvent.Metadata["command_topic"].ToString() ?? ""
            : "";
        var stateTopic = discoveryEvent.Metadata.ContainsKey("state_topic")
            ? discoveryEvent.Metadata["state_topic"].ToString() ?? ""
            : "";

        // Create new device
        var newDevice = new Device
        {
            Id = discoveryEvent.DeviceId,
            Name = discoveryEvent.Name,
            State = discoveryEvent.State,
            LastUpdated = DateTime.UtcNow
        };

        // Save to DB
        await SaveToDb(newDevice, "devices");

        // Add to cache
        _devices[newDevice.Id] = newDevice;
        _deviceLastModified[newDevice.Id] = DateTime.UtcNow;

        Logger.LogInformation("Registered new Generic device: {DeviceName} ({DeviceId})",
            newDevice.Name, newDevice.Id);
    }

    public override void SetupMessageBusSubscriptions()
    {
        SubscribeToCommands<DeviceCommand>(
            MessageBusConfiguration.DeviceCommandsQueue,
            HandleDeviceCommand);

        SubscribeToEvents<DeviceEvent>(
            MessageBusConfiguration.DeviceEventsQueue,
            HandleDeviceEvent);

        // Subscribe to device discovery events from DiscoveryService
        SubscribeToEvents<DeviceDiscoveredEvent>(
            MessageBusConfiguration.DeviceDiscoveryEventsQueue,
            HandleDeviceDiscovered);
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

        // Update device state in the database directly
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

        var configChanged = false;

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
    /// Updates the state of a device and publishes a state update event to the message bus
    /// </summary>
    public override async Task UpdateDeviceState(string deviceId, Dictionary<string, object> state)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));

        if (state == null)
            throw new ArgumentNullException(nameof(state));

        try
        {
            // Save state directly to DB
            var stateUpdate = new DeviceStateUpdate
            {
                DeviceId = deviceId,
                State = state,
                Timestamp = DateTime.UtcNow,
                Name = $"State_{deviceId}_{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}"
            };

            await SaveToDb(stateUpdate, "devices/state");

            // Publish state update event
            var stateEvent = new DeviceStateUpdatedEvent
            {
                DeviceId = deviceId,
                State = state,
                Timestamp = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid(),
                Success = true
            };

            stateEvent.Data["Source"] = GetType().Name;

            await PublishEvent(
                Options.EventExchange,
                stateEvent,
                "event.device.state.updated"
            );

            Logger.LogDebug("Updated state for device {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating state for device {DeviceId}", deviceId);
            throw;
        }
    }

    /// <summary>
    /// Updates the online status of a device
    /// </summary>
    public override async Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));

        try
        {
            // In a real implementation, we would update the device status in the DB
            // For now, we just publish an event

            await PublishEvent(
                Options.EventExchange,
                new DeviceEvent
                {
                    DeviceId = new Guid(deviceId),
                    EventType = DeviceEventTypes.StatusChanged,
                    Data = new Dictionary<string, object>
                    {
                        { "status", isOnline ? "online" : "offline" },
                        { "isOnline", isOnline }
                    },
                    Source = GetType().Name,
                    Timestamp = DateTime.UtcNow
                },
                "event.device.status.changed"
            );

            Logger.LogDebug("Updated online status for device {DeviceId} to {Status}", deviceId, isOnline);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating online status for device {DeviceId}", deviceId);
            throw;
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
