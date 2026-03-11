using System.Text.Json;

using Domovoy.Common.Models;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Events;

using Microsoft.Extensions.Logging;

namespace Domovoy.Common.Services.Handlers;

/// <summary>
/// Handler for Light device types.
/// Manages typed LightState (IsOn, Brightness, Color, ColorTemperature)
/// with safe JsonElement parsing for deserialized MQTT payloads.
/// </summary>
public class LightDeviceHandler : IDeviceTypeHandler
{
    public GlobalEntityTypes HandledType => GlobalEntityTypes.Light;

    public BaseEntity CreateFromDiscovery(DeviceDiscoveredEvent discoveryEvent)
    {
        var light = new Light
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

        if (discoveryEvent.Metadata != null)
        {
            foreach (var kvp in discoveryEvent.Metadata)
            {
                light.Metadata[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        }

        // Ensure Adapter Source is saved
        if (!string.IsNullOrEmpty(discoveryEvent.Source))
        {
            light.Metadata["AdapterSource"] = discoveryEvent.Source;
        }

        return light;
    }

    public Task HandleCommand(BaseEntity device, DeviceCommand command, ILogger logger)
    {
        if (device is not Light light)
        {
            logger.LogWarning("LightDeviceHandler received a non-Light entity: {Type}", device.GetType().Name);
            return Task.CompletedTask;
        }

        logger.LogInformation("Handling command {CommandType} for light {DeviceId}",
            command.CommandTypes, light.Id);

        // TODO: Implement light-specific command handling (e.g., TurnOn, TurnOff, SetBrightness, SetColor)
        return Task.CompletedTask;
    }

    public Task HandleEvent(BaseEntity device, BaseEvent @event, ILogger logger)
    {
        // TODO: Implement light-specific event handling
        return Task.CompletedTask;
    }

    public Task UpdateState(BaseEntity device, Dictionary<string, object> state)
    {
        if (device is not Light light) return Task.CompletedTask;

        // Parse IsOn
        if (state.TryGetValue("IsOn", out var isOnValue))
        {
            light.State.IsOn = isOnValue switch
            {
                JsonElement jsonEl => jsonEl.GetBoolean(),
                bool boolVal => boolVal,
                _ => light.State.IsOn
            };
        }

        // Parse Brightness
        if (state.TryGetValue("Brightness", out var brightnessValue))
        {
            light.State.Brightness = brightnessValue switch
            {
                JsonElement jsonEl => jsonEl.GetInt32(),
                int intVal => intVal,
                _ => light.State.Brightness
            };
        }

        // Parse Color
        if (state.TryGetValue("Color", out var colorValue))
        {
            light.State.Color = colorValue switch
            {
                JsonElement jsonEl => jsonEl.GetString() ?? "#FFFFFF",
                string strVal => strVal,
                _ => light.State.Color
            };
        }

        // Parse ColorTemperature
        if (state.TryGetValue("ColorTemperature", out var tempValue))
        {
            light.State.ColorTemperature = tempValue switch
            {
                JsonElement jsonEl => jsonEl.GetInt32(),
                int intVal => intVal,
                _ => light.State.ColorTemperature
            };
        }

        light.LastUpdated = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task UpdateOnlineStatus(BaseEntity device, bool isOnline)
    {
        if (isOnline)
        {
            device.LastUpdated = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }
}
