using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Services;
using Domovoy.MessageBus;

using Domovoy.Common.Configuration;
using Microsoft.Extensions.Options;

namespace Domovoy.SensorService.Services
{
    /// <summary>
    /// Manages sensor devices in the Domovoy system.
    /// This service handles sensor-specific commands, events, and state management
    /// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
    /// Also provides support for MQTT-based sensor devices.
    /// </summary>
    public class SensorManager : BaseService
    {
        private readonly Dictionary<string, Sensor> _sensors = new();
        private readonly Dictionary<string, DateTime> _sensorLastModified = new();
        private readonly Dictionary<string, Timer> _reportingTimers = new();
        private readonly Dictionary<string, SensorReportingConfig> _reportingConfigs = new();

        /// <summary>
        /// Initializes a new instance of the SensorManager class.
        /// </summary>
        /// <param name="messageBus">The message bus for pubsub operations</param>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger for the service</param>
        /// <param name="options">Service configuration options</param>
        public SensorManager(
            IMessageBus messageBus,
            IHttpClientFactory httpClientFactory,
            ILogger<SensorManager> logger,
            IOptions<BaseServiceOptions> options)
            : base(messageBus, httpClientFactory, logger, options)
        {
        }

        public override async Task HandleDeviceCommand(BaseCommand command)
        {
            if (command is not DeviceCommand deviceCommand) return;
            // Sensors usually don't accept commands, but might accept configuration commands
            await Task.CompletedTask;
        }

        public override async Task HandleDeviceEvent(BaseEvent @event)
        {
            // TODO: Implement sensor specific event handling
            await Task.CompletedTask;
        }

        public override void SetupMessageBusSubscriptions()
        {
            // Subscribe to device discovery events
            SubscribeToEvents<DeviceDiscoveredEvent>(
                MessageBusConfiguration.DeviceDiscoveryEventsQueue,
                HandleDeviceDiscovered);
        }

        private async Task HandleDeviceDiscovered(DeviceDiscoveredEvent discoveryEvent)
        {
            // Filter: Only handle Sensor devices
            if (discoveryEvent.DeviceType != GlobalEntityTypes.Sensor)
            {
                return;
            }

            Logger.LogInformation("Received sensor discovery: {DeviceName} ({DeviceId})",
                discoveryEvent.Name, discoveryEvent.DeviceId);

            // Check if device already exists
            if (_sensors.ContainsKey(discoveryEvent.DeviceId.ToString()))
            {
                Logger.LogInformation("Sensor already registered: {DeviceId}", discoveryEvent.DeviceId);
                return;
            }

            // Create new sensor device
            var newSensor = new Sensor
            {
                Id = discoveryEvent.DeviceId,
                Name = discoveryEvent.Name,
                Type = SensorTypes.MultiSensor, // Default to MultiSensor as Generic is not available
                LastUpdated = DateTime.UtcNow,
                State = new SensorState()
            };

            // Convert metadata
            if (discoveryEvent.Metadata != null)
            {
                foreach (var kvp in discoveryEvent.Metadata)
                {
                    newSensor.Metadata[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }

            // Save to DB
            _sensors[newSensor.Id.ToString()] = newSensor;
            _sensorLastModified[newSensor.Id.ToString()] = DateTime.UtcNow;

            Logger.LogInformation("Registered new Sensor: {DeviceName} ({DeviceId})",
                newSensor.Name, newSensor.Id);

            await Task.CompletedTask;
        }

        public override Task UpdateDeviceState(string deviceId, Dictionary<string, object> state)
        {
            if (_sensors.TryGetValue(deviceId, out var sensor))
            {
                // Update sensor state logic here
                sensor.LastUpdated = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public override Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline)
        {
            if (_sensors.TryGetValue(deviceId, out var sensor))
            {
                if (isOnline)
                {
                    sensor.LastUpdated = DateTime.UtcNow;
                }
            }
            return Task.CompletedTask;
        }
    }
}
