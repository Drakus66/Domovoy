namespace Domovoy.UnifiedDeviceService.Services;

using Domovoy.Common.Models;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Enums;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Services;
using Domovoy.MessageBus;
using Domovoy.Common.Configuration;
using Domovoy.UnifiedDeviceService.Services.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

/// <summary>
/// Unified device manager that handles all device types (Generic, Light, Sensor)
/// using the IDeviceTypeHandler strategy pattern.
/// Based on the original DeviceManager with full DB, event, and MQTT support.
/// </summary>
public class UnifiedDeviceManager : BaseService
{
    private const string DevicesCollection = "devices";
    private const string DeviceStateCollection = "devices/state";
    
    private readonly Dictionary<Guid, BaseEntity> _devices = new();
    private readonly Dictionary<Guid, DateTime> _deviceLastModified = new();
    private readonly Dictionary<GlobalEntityTypes, IDeviceTypeHandler> _handlers;
    private readonly IDeviceIdentityResolver _identityResolver;

    /// <summary>
    /// Initializes a new instance of the UnifiedDeviceManager class.
    /// </summary>
    public UnifiedDeviceManager(
        IMessageBus messageBus,
        IHttpClientFactory httpClientFactory,
        ILogger<UnifiedDeviceManager> logger,
        IOptions<BaseServiceOptions> options,
        IEnumerable<IDeviceTypeHandler> handlers,
        IDeviceIdentityResolver identityResolver)
        : base(messageBus, httpClientFactory, logger, options)
    {
        // Build a dictionary of handlers keyed by their handled device type
        _handlers = handlers.ToDictionary(h => h.HandledType, h => h);
        _identityResolver = identityResolver;

        Logger.LogInformation("UnifiedDeviceManager initialized with handlers for: {Types}",
            string.Join(", ", _handlers.Keys));
    }

    /// <summary>
    /// Returns the handler for the given device type, or null if not registered.
    /// </summary>
    private IDeviceTypeHandler? GetHandler(GlobalEntityTypes type)
    {
        if (_handlers.TryGetValue(type, out var handler))
            return handler;

        Logger.LogWarning("No handler registered for device type: {Type}", type);
        return null;
    }

    /// <summary>
    /// Determines the GlobalEntityTypes for a cached device entity.
    /// </summary>
    private static GlobalEntityTypes GetDeviceType(BaseEntity device)
    {
        return device switch
        {
            Light => GlobalEntityTypes.Light,
            Sensor => GlobalEntityTypes.Sensor,
            Device => GlobalEntityTypes.Generic,
            _ => GlobalEntityTypes.Generic
        };
    }

    // ========================================================================
    // Message Bus Subscriptions
    // ========================================================================

    public override void SetupMessageBusSubscriptions()
    {
        // Subscribe to device commands (unified queue)
        SubscribeToCommands<DeviceCommand>(
            MessageBusConfiguration.DeviceCommandsQueue,
            HandleDeviceCommand);

        // Subscribe to device events
        SubscribeToEvents<DeviceEvent>(
            MessageBusConfiguration.DeviceEventsQueue,
            HandleDeviceEvent);

        // Subscribe to device discovery events from DiscoveryService
        MessageBus.SubscribeAsync<DeviceDiscoveredEvent>(
            MessageBusConfiguration.DeviceDiscoveryEventsQueue, // Queue name
            MessageBusConfiguration.DeviceDiscoveryExchange, // Exchange
            MessageBusConfiguration.DeviceDiscoveredRoutingKey, // Routing key
            HandleDeviceDiscovered, 
            default); // CancellationToken

        // Subscribe to light-specific commands queue (backward compatibility)
        SubscribeToCommands<DeviceCommand>(
            "domovoy.light.commands",
            HandleDeviceCommand);

        // Subscribe to raw adapter state reports from Connectivity Service.
        // Exchange/routing key come from shared constants so they always match the publisher
        // (AdapterManager). Previously this was bound to "domovoy.events" while the publisher
        // sent to "domovoy.state", so device state never arrived.
        MessageBus.SubscribeAsync<AdapterStateReportedEvent>(
            MessageBusConfiguration.AdapterStateReportsQueue,
            MessageBusConfiguration.AdapterStateExchange,
            MessageBusConfiguration.AdapterStateReportedRoutingKey,
            HandleAdapterStateReported,
            default); // CancellationToken
            
        Logger.LogInformation("UnifiedDeviceManager subscribed to all device queues");
    }

    // ========================================================================
    // Discovery Handling
    // ========================================================================

    /// <summary>
    /// Handles device discovery events for ALL device types.
    /// Delegates creation to the appropriate IDeviceTypeHandler.
    /// </summary>
    public async Task HandleDeviceDiscovered(DeviceDiscoveredEvent discoveryEvent)
    {
        var handler = GetHandler(discoveryEvent.DeviceType);
        if (handler == null)
        {
            Logger.LogDebug("No handler for device type {DeviceType}, ignoring discovery", discoveryEvent.DeviceType);
            return;
        }

        Logger.LogInformation("Received {DeviceType} discovery: {DeviceName} ({DeviceId})",
            discoveryEvent.DeviceType, discoveryEvent.Name, discoveryEvent.DeviceId);

        // Check if device already exists in cache or by IEEE to support renaming
        Guid targetDeviceId = discoveryEvent.DeviceId;

        // Extract IEEE to see if it's an existing device that was renamed
        if (discoveryEvent.Metadata != null && discoveryEvent.Metadata.TryGetValue("ieee_address", out var ieeeObj) && ieeeObj is string ieee)
        {
            var resolvedId = _identityResolver.ResolveDevice(discoveryEvent.Source, ieee);
            if (resolvedId != null)
            {
                targetDeviceId = resolvedId.Value;
                discoveryEvent.DeviceId = targetDeviceId; // Make sure event aligns with system existing device
                
                Logger.LogInformation("Resolved discovery to existing device {DeviceId} via IEEE {IEEE}. Processing as update/rename.", targetDeviceId, ieee);
            }
        }

        if (!_devices.ContainsKey(targetDeviceId))
        {
            await LoadDeviceFromDb(targetDeviceId.ToString());
        }

        if (_devices.TryGetValue(targetDeviceId, out var existingDevice))
        {
            Logger.LogInformation("Updating existing device: {DeviceId}", targetDeviceId);
            
            // Apply name changes (Rename feature)
            if (existingDevice.Name != discoveryEvent.Name)
            {
                Logger.LogInformation("Device {DeviceId} renamed: {OldName} -> {NewName}", targetDeviceId, existingDevice.Name, discoveryEvent.Name);
                existingDevice.Name = discoveryEvent.Name;
            }

            // Apply metadata changes (like state/command topics changing after rename)
            if (discoveryEvent.Metadata != null)
            {
                foreach(var meta in discoveryEvent.Metadata)
                {
                    existingDevice.Metadata[meta.Key] = meta.Value?.ToString() ?? "";
                }
            }

            // Persist/refresh the adapter source so commands can be routed back to the right
            // adapter even on a cold AdapterManager cache (mirrors IDeviceTypeHandler.CreateFromDiscovery).
            // Without this, existing devices loaded from the DB had no AdapterSource and their
            // commands were dropped by the Connectivity service.
            if (!string.IsNullOrEmpty(discoveryEvent.Source))
            {
                existingDevice.Metadata["AdapterSource"] = discoveryEvent.Source;
            }

            existingDevice.LastUpdated = DateTime.UtcNow;
            await SaveToDb(existingDevice, DevicesCollection);

            // Re-register with identity resolver so the new topic is mapped, 
            // the old topic mapping might still linger until reboot, but that is fine.
            _identityResolver.RegisterDevice(existingDevice);
            return;
        }

        // Create new device via handler
        var newDevice = handler.CreateFromDiscovery(discoveryEvent);

        // Save to DB
        await SaveToDb(newDevice, DevicesCollection);

        // Add to cache
        _devices[newDevice.Id] = newDevice;
        _deviceLastModified[newDevice.Id] = DateTime.UtcNow;
        _identityResolver.RegisterDevice(newDevice);

        Logger.LogInformation("Registered new {DeviceType} device: {DeviceName} ({DeviceId})",
            discoveryEvent.DeviceType, newDevice.Name, newDevice.Id);
    }

    // ========================================================================
    // Command Handling
    // ========================================================================

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
            // Delegate to the appropriate handler (for local state updates before persisting)
            var deviceType = GetDeviceType(device);
            var handler = GetHandler(deviceType);

            if (handler != null)
            {
                await handler.HandleCommand(device, command, Logger);
            }
            else
            {
                Logger.LogWarning("No handler for device type {DeviceType}, device {DeviceId}. Not processing locally.",
                    deviceType, deviceId);
            }

            // Mark device as modified locally. 
            _deviceLastModified[deviceId] = DateTime.UtcNow;

            // Forward command to physical adapters (Connectivity Service)
            // Enrich with metadata like "command_topic" so adapters know where to send it
            if (device.Metadata != null)
            {
                device.Metadata
                    .Where(kvp => !command.Parameters.ContainsKey(kvp.Key))
                    .ToList()
                    .ForEach(kvp => command.Parameters[kvp.Key] = kvp.Value);
            }

            // Publish enriched command to Connectivity Service Queue
            // Re-using the same exchange, but relying on Connectivity to pick it up
            await MessageBus.PublishAsync(
                MessageBusConfiguration.DeviceCommandsExchange,
                MessageBusConfiguration.DeviceCommandsQueue, // Or specific routing key for adapters
                command);

            Logger.LogInformation("Forwarded command for device {DeviceId} to Connectivity Service", deviceId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing command {CommandType} for device {DeviceId}",
                command.CommandTypes, deviceId);

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

    // ========================================================================
    // Event Handling
    // ========================================================================

    public override async Task HandleDeviceEvent(BaseEvent @event)
    {
        if (@event is DeviceEvent deviceEvent && _devices.TryGetValue(deviceEvent.DeviceId, out var device))
        {
            var deviceType = GetDeviceType(device);
            var handler = GetHandler(deviceType);
            if (handler != null)
            {
                await handler.HandleEvent(device, @event, Logger);
            }
        }
    }

    /// <summary>
    /// Handles raw state reports from connectivity adapters (Option 2 Lazy Routing).
    /// </summary>
    private async Task HandleAdapterStateReported(AdapterStateReportedEvent @event)
    {
        var deviceId = _identityResolver.ResolveDevice(@event.AdapterSource, @event.Topic);
        if (deviceId == null)
        {
            Logger.LogWarning("Could not resolve device for {AdapterSource} / {Topic}", @event.AdapterSource, @event.Topic);
            return;
        }

        Logger.LogInformation("Resolved {Topic} to Device {DeviceId}", @event.Topic, deviceId);

        try
        {
            // Parse payload string as dictionary
            var stateDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(@event.Payload);
            if (stateDict != null)
            {
                await UpdateDeviceState(deviceId.Value.ToString(), stateDict);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse and update state from {Topic} payload: {Payload}", @event.Topic, @event.Payload);
        }
    }

    // ========================================================================
    // State Management
    // ========================================================================

    public override async Task UpdateDeviceState(string deviceId, Dictionary<string, object> state)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        try
        {
            // If device is in cache, delegate to handler for type-specific state update
            var guid = new Guid(deviceId);
            if (_devices.TryGetValue(guid, out var device))
            {
                var deviceType = GetDeviceType(device);
                var handler = GetHandler(deviceType);
                if (handler != null)
                {
                    await handler.UpdateState(device, state);
                }
            }

            // Save state to DB
            var stateUpdate = new DeviceStateUpdate
            {
                DeviceId = deviceId,
                State = state,
                Timestamp = DateTime.UtcNow,
                Name = $"State_{deviceId}_{DateTime.UtcNow:yyyyMMddHHmmss}"
            };

            await SaveToDb(stateUpdate, DeviceStateCollection);

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

            // Routing key uses the shared constant so DbGateway.EventInterceptor (persistence)
            // and ApiGateway.EventRelayService (SignalR) — which bind this same constant — actually
            // receive the event. Previously the literal "event.device.state.updated" never matched
            // their "device.state.updated" binding on the topic exchange.
            await PublishEvent(
                Options.EventExchange,
                stateEvent,
                MessageBusConfiguration.DeviceStateUpdatedRoutingKey
            );

            Logger.LogDebug("Updated state for device {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating state for device {DeviceId}", deviceId);
            throw;
        }
    }

    public override async Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));

        try
        {
            // If device is in cache, delegate to handler
            var guid = new Guid(deviceId);
            if (_devices.TryGetValue(guid, out var device))
            {
                var deviceType = GetDeviceType(device);
                var handler = GetHandler(deviceType);
                if (handler != null)
                {
                    await handler.UpdateOnlineStatus(device, isOnline);
                }
            }

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

    // ========================================================================
    // Device Access (Public API)
    // ========================================================================

    /// <summary>
    /// Gets a device by its ID, loading it from the database if necessary.
    /// </summary>
    public async Task<BaseEntity?> GetDeviceAsync(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentNullException(nameof(deviceId));

        var guid = new Guid(deviceId);

        if (_devices.TryGetValue(guid, out var device))
            return device;

        await LoadDeviceFromDb(deviceId);

        return _devices.TryGetValue(guid, out device) ? device : null;
    }

    /// <summary>
    /// Registers a new device in the system.
    /// </summary>
    public async Task<BaseEntity> RegisterDeviceAsync(BaseEntity newDevice)
    {
        if (newDevice == null)
            throw new ArgumentNullException(nameof(newDevice));
        if (string.IsNullOrEmpty(newDevice.Name))
            throw new ArgumentException("Device name cannot be empty");

        newDevice.LastUpdated = DateTime.UtcNow;

        await SaveToDb(newDevice, DevicesCollection);

        _devices[newDevice.Id] = newDevice;
        _deviceLastModified[newDevice.Id] = DateTime.UtcNow;
        _identityResolver.RegisterDevice(newDevice);

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

    // ========================================================================
    // Database Operations
    // ========================================================================

    /// <summary>
    /// Loads a device from the database via DbGateway.
    /// </summary>
    private async Task LoadDeviceFromDb(string deviceId)
    {
        try
        {
            Logger.LogDebug("Loading device {DeviceId} from database", deviceId);

            // Try loading as Device (generic), which covers MqttDevice too
            var device = await LoadFromDb<Device>(deviceId, DevicesCollection);

            if (device != null)
            {
                _devices[new Guid(deviceId)] = device;
                _identityResolver.RegisterDevice(device);
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
    /// Publishes a state changed event for a device.
    /// </summary>
    private async Task PublishStateChanged(BaseEntity device, Guid? correlationId = null)
    {
        try
        {
            await PublishEvent(
                Options.EventExchange,
                new DeviceEvent
                {
                    DeviceId = device.Id,
                    EventType = DeviceEventTypes.StateChanged,
                    Data = new Dictionary<string, object>
                    {
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
}
