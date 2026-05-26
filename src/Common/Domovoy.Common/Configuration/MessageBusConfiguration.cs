namespace Domovoy.Common.Configuration;

/// <summary>
/// Provides centralized configuration constants for message bus exchanges and routing keys.
/// This class defines standard naming conventions for the Domovoy system's message bus infrastructure.
/// </summary>
public class MessageBusConfiguration
{
    /// <summary>
    /// Exchange for device discovery and announcements.
    /// </summary>
    public const string DeviceDiscoveryExchange = "domovoy/discovery";
    
    /// <summary>
    /// Exchange for device availability and online status.
    /// </summary>
    public const string DeviceAvailabilityExchange = "domovoy/availability";
    
    /// <summary>
    /// Exchange for device-related commands.
    /// </summary>
    public const string DeviceCommandsExchange = "device.commands";

    /// <summary>
    /// Exchange for device-related events.
    /// </summary>
    public const string DeviceEventsExchange = "device.events";

    /// <summary>
    /// Exchange for device telemetry and data.
    /// </summary>
    public const string DeviceDataExchange = "device.data";

    /// <summary>
    /// Routing key for device state update commands.
    /// </summary>
    public const string DeviceStateUpdateRoutingKey = "device.state.update";

    /// <summary>
    /// Routing key for device online status change commands.
    /// </summary>
    public const string DeviceOnlineChangeRoutingKey = "device.online.change";

    /// <summary>
    /// Routing key for device creation commands.
    /// </summary>
    public const string DeviceCreateRoutingKey = "device.create";

    /// <summary>
    /// Routing key for device update commands.
    /// </summary>
    public const string DeviceUpdateRoutingKey = "device.update";

    /// <summary>
    /// Routing key for device deletion commands.
    /// </summary>
    public const string DeviceDeleteRoutingKey = "device.delete";

    /// <summary>
    /// Routing key for device state updated events.
    /// </summary>
    public const string DeviceStateUpdatedRoutingKey = "device.state.updated";

    /// <summary>
    /// Routing key for device online status changed events.
    /// </summary>
    public const string DeviceOnlineChangedRoutingKey = "device.online.changed";

    /// <summary>
    /// Routing key for device created events.
    /// </summary>
    public const string DeviceCreatedRoutingKey = "device.created";

    /// <summary>
    /// Routing key for device updated events.
    /// </summary>
    public const string DeviceUpdatedRoutingKey = "device.updated";

    /// <summary>
    /// Routing key for device deleted events.
    /// </summary>
    public const string DeviceDeletedRoutingKey = "device.deleted";

    public const string DeviceErrorRoutingKey = "device.error";
    public const string DeviceDataRoutingKey = "device.data";
    
    // MQTT Discovery related routing keys
    public const string DeviceAnnouncementRoutingKey = "domovoy/device/announce";
    public const string DeviceDiscoveryRoutingKey = "domovoy/device/discovery";
    public const string DeviceAvailabilityRoutingKey = "domovoy/device/available";
    public const string DeviceStatusRoutingKey = "domovoy/device/status";
    
    // Home Assistant MQTT Discovery prefix and patterns
    public const string HomeAssistantDiscoveryPrefix = "homeassistant";
    public const string HomeAssistantSensorPattern = "homeassistant/sensor/{0}/config";
    public const string HomeAssistantSwitchPattern = "homeassistant/switch/{0}/config";
    public const string HomeAssistantLightPattern = "homeassistant/light/{0}/config";
    public const string HomeAssistantBinarySensorPattern = "homeassistant/binary_sensor/{0}/config";
    
    // Очереди
    public const string SensorCommandsQueue = "sensor.commands.queue";
    public const string SensorEventsQueue = "sensor.events.queue";
    public const string LightCommandsQueue = "light.commands.queue";
    public const string LightEventsQueue = "light.events.queue";
    public const string DeviceCommandsQueue = "device.commands.queue";
    public const string DeviceEventsQueue = "device.events.queue";
    public const string DeviceDiscoveryCommandsQueue = "device.discovery.commands.queue";
    public const string DeviceDiscoveryEventsQueue = "device.discovery.events.queue";
    
    // Discovery и регистрация устройств
    public const string DeviceDiscoveredRoutingKey = "device.discovered";
    public const string DeviceHeartbeatRoutingKey = "device.heartbeat";
    public const string DeviceConfigurationRoutingKey = "device.configure";
    
    // Zigbee bridge events & commands
    public const string ZigbeeBridgeExchange = "zigbee.bridge";
    public const string ZigbeeBridgeStateRoutingKey = "zigbee.bridge.state";
    public const string ZigbeeBridgeInfoRoutingKey = "zigbee.bridge.info";
    public const string ZigbeeNetworkEventRoutingKey = "zigbee.network.event";
    public const string ZigbeeBridgeCommandsQueue = "zigbee.bridge.commands.queue";
    public const string ZigbeeBridgeCommandRoutingKey = "zigbee.bridge.command";

    // Raw adapter state reports (Connectivity -> UnifiedDeviceService)
    // Publisher (AdapterManager) and subscriber (UnifiedDeviceManager) MUST reference these
    // same constants so the exchange/routing key always match.
    public const string AdapterStateExchange = "domovoy.state";
    public const string AdapterStateReportsQueue = "domovoy-adapter-reports";
    public const string AdapterStateReportedRoutingKey = "event.adapter.reported";

    // Generic device control commands (ApiGateway -> UnifiedDeviceService).
    // Must match the "command.*" binding used by BaseService.SubscribeToCommands.
    public const string DeviceControlCommandRoutingKey = "command.device";

    // MQTT шаблоны топиков
    public const string MqttDeviceCommandPattern = "domovoy/{0}/command";
    public const string MqttDeviceStatePattern = "domovoy/{0}/state";
    public const string MqttDeviceAvailabilityPattern = "domovoy/{0}/availability";
}