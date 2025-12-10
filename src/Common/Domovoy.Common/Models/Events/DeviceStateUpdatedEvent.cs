namespace Domovoy.Common.Models.Events
{
    /// <summary>
    /// Event class for device state updates
    /// </summary>
    public class DeviceStateUpdatedEvent : BaseEvent
    {
        public string DeviceId { get; set; } = null!;
        public Dictionary<string, object> State { get; set; } = new();
    }
}
