using Domovoy.Common.Models.Enums;

namespace Domovoy.Common.Models.Events;

public class LightEvent : BaseEvent
{
    public required Guid LightId { get; set; }

    /// <summary>
    /// Defines the types of events that can be emitted by light devices.
    /// </summary>
    public LightEventTypes EventType { get; set; }
}