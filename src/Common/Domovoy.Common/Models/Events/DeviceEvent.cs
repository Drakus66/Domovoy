using Domovoy.Common.Models.Enums;

namespace Domovoy.Common.Models.Events
{
    public class DeviceEvent : BaseEvent
    {
        public required Guid DeviceId { get; set; }
        public required DeviceEventTypes EventType { get; set; }
    }
}