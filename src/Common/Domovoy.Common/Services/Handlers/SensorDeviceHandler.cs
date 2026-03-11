using Domovoy.Common.Models;
using Domovoy.Common.Models.Commands;
using Domovoy.Common.Models.Devices;
using Domovoy.Common.Models.Enums.EntityTypes;
using Domovoy.Common.Models.Events;

using Microsoft.Extensions.Logging;

namespace Domovoy.Common.Services.Handlers;

/// <summary>
/// Handler for Sensor device types.
/// Manages typed SensorState (Temperature, Humidity, Motion, etc.).
/// Sensors typically don't accept commands but report telemetry data.
/// </summary>
public class SensorDeviceHandler : IDeviceTypeHandler
{
    public GlobalEntityTypes HandledType => GlobalEntityTypes.Sensor;

    public BaseEntity CreateFromDiscovery(DeviceDiscoveredEvent discoveryEvent)
    {
        var sensor = new Sensor
        {
            Id = discoveryEvent.DeviceId,
            Name = discoveryEvent.Name,
            Type = SensorTypes.MultiSensor,
            LastUpdated = DateTime.UtcNow,
            State = new SensorState()
        };

        if (discoveryEvent.Metadata != null)
        {
            foreach (var kvp in discoveryEvent.Metadata)
            {
                sensor.Metadata[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        }

        // Ensure Adapter Source is saved
        if (!string.IsNullOrEmpty(discoveryEvent.Source))
        {
            sensor.Metadata["AdapterSource"] = discoveryEvent.Source;
        }

        return sensor;
    }

    public Task HandleCommand(BaseEntity device, DeviceCommand command, ILogger logger)
    {
        // Sensors typically don't accept commands, but can accept configuration commands
        logger.LogDebug("Sensor {DeviceId} received command {CommandType} — sensors usually don't accept commands",
            device.Id, command.CommandTypes);
        return Task.CompletedTask;
    }

    public Task HandleEvent(BaseEntity device, BaseEvent @event, ILogger logger)
    {
        // TODO: Implement sensor-specific event handling (e.g., threshold alerts)
        return Task.CompletedTask;
    }

    public Task UpdateState(BaseEntity device, Dictionary<string, object> state)
    {
        if (device is not Sensor sensor) return Task.CompletedTask;

        // TODO: Parse sensor-specific state fields from dictionary into SensorState
        // (Temperature, Humidity, MotionDetected, LightLevel, AirQuality, Pressure, BatteryLevel)
        sensor.LastUpdated = DateTime.UtcNow;
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
