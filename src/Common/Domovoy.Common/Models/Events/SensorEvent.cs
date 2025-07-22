using Domovoy.Common.Models.Enums;

namespace Domovoy.Common.Models.Events;

public class SensorEvent : BaseEvent
{
    public required Guid SensorId { get; set; }

    /// <summary>
    /// Defines the types of events that can be emitted by sensor devices.
    /// </summary>
    public SensorEventTypes EventType { get; set; }
}