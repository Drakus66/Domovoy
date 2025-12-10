using System.Text.Json;

using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Events;
using Domovoy.Common.Services;
using Domovoy.MessageBus;

using Domovoy.Common.Configuration;
using Microsoft.Extensions.Options;

namespace Domovoy.LightService.Services
{
    /// <summary>
    /// Manages light devices in the Domovoy system.
    /// This service handles light-specific commands, events, and state management
    /// following the Gateway Pattern architecture where DB access is centralized through DbGateway.
    /// Also provides support for MQTT-based light devices.
    /// </summary>
    public class LightManager : BaseService
    {
        private readonly Dictionary<string, Light> _lights = new();
        private readonly Dictionary<string, DateTime> _lightLastModified = new();

        /// <summary>
        /// Initializes a new instance of the LightManager class.
        /// </summary>
        /// <param name="messageBus">The message bus for pubsub operations</param>
        /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
        /// <param name="logger">Logger for the service</param>
        /// <param name="options">Service configuration options</param>
        public LightManager(
            IMessageBus messageBus,
            IHttpClientFactory httpClientFactory,
            ILogger<LightManager> logger,
            IOptions<BaseServiceOptions> options)
            : base(messageBus, httpClientFactory, logger, options)
        {
        }

        public override async Task HandleDeviceCommand(BaseCommand command)
        {
            if (command is not DeviceCommand deviceCommand) return;

            Logger.LogInformation("Handling command {CommandType} for light {DeviceId}", deviceCommand.CommandTypes, deviceCommand.DeviceId);

            // TODO: Implement light specific command handling
            await Task.CompletedTask;
        }

        public override async Task HandleDeviceEvent(BaseEvent @event)
        {
            // TODO: Implement light specific event handling
            await Task.CompletedTask;
        }

        public override void SetupMessageBusSubscriptions()
        {
            // Subscribe to device discovery events
            SubscribeToEvents<DeviceDiscoveredEvent>(
                MessageBusConfiguration.DeviceDiscoveryEventsQueue,
                HandleDeviceDiscovered);

            // Subscribe to commands
            SubscribeToCommands<DeviceCommand>(
                "domovoy.light.commands", // Dedicated queue for light commands
                HandleDeviceCommand);
        }

        private async Task HandleDeviceDiscovered(DeviceDiscoveredEvent discoveryEvent)
        {
            // Filter: Only handle Light devices
            if (discoveryEvent.DeviceType != Common.Models.Enums.EntityTypes.GlobalEntityTypes.Light)
            {
                return;
            }

            Logger.LogInformation("Received light discovery: {DeviceName} ({DeviceId})",
                discoveryEvent.Name, discoveryEvent.DeviceId);

            // Check if device already exists
            if (_lights.ContainsKey(discoveryEvent.DeviceId.ToString()))
            {
                Logger.LogInformation("Light already registered: {DeviceId}", discoveryEvent.DeviceId);
                return;
            }

            // Create new light device
            var newLight = new Light
            {
                Id = discoveryEvent.DeviceId,
                Name = discoveryEvent.Name,
                LastUpdated = DateTime.UtcNow,
                State = new LightState
                {
                    IsOn = false,
                    Brightness = 0,
                    Color = "#FFFFFF"
                }
            };

            // Convert metadata
            if (discoveryEvent.Metadata != null)
            {
                foreach (var kvp in discoveryEvent.Metadata)
                {
                    newLight.Metadata[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }

            // Save to DB (via DbGateway or direct if needed, but we should use DbGateway pattern eventually)
            // For now, just cache it as per the stub
            _lights[newLight.Id.ToString()] = newLight;
            _lightLastModified[newLight.Id.ToString()] = DateTime.UtcNow;

            Logger.LogInformation("Registered new Light: {DeviceName} ({DeviceId})",
                newLight.Name, newLight.Id);

            await Task.CompletedTask;
        }

        public override Task UpdateDeviceState(string deviceId, Dictionary<string, object> state)
        {
            if (_lights.TryGetValue(deviceId, out var light))
            {
                // Update state properties safely
                if (state.ContainsKey("IsOn") && state["IsOn"] is JsonElement isOnElement)
                {
                    light.State.IsOn = isOnElement.GetBoolean();
                }
                else if (state.ContainsKey("IsOn") && state["IsOn"] is bool isOn)
                {
                    light.State.IsOn = isOn;
                }

                if (state.ContainsKey("Brightness") && state["Brightness"] is JsonElement brightnessElement)
                {
                    light.State.Brightness = brightnessElement.GetInt32();
                }
                else if (state.ContainsKey("Brightness") && state["Brightness"] is int brightness)
                {
                    light.State.Brightness = brightness;
                }

                if (state.ContainsKey("Color") && state["Color"] is JsonElement colorElement)
                {
                    light.State.Color = colorElement.GetString() ?? "#FFFFFF";
                }
                else if (state.ContainsKey("Color") && state["Color"] is string color)
                {
                    light.State.Color = color;
                }

                light.LastUpdated = DateTime.UtcNow;
                // TODO: Persist state
            }
            return Task.CompletedTask;
        }

        public override Task UpdateDeviceOnlineStatus(string deviceId, bool isOnline)
        {
            if (_lights.TryGetValue(deviceId, out var light) && isOnline)
            {
                // IsOnline is computed from LastUpdated, so we just update LastUpdated if online
                light.LastUpdated = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }
    }
}
