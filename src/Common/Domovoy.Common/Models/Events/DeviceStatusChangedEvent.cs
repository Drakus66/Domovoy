namespace Domovoy.Common.Models.Events
{
    /// <summary>
    /// Event class for device status changes
    /// </summary>
    public class DeviceStatusChangedEvent : BaseEvent
    {
        public string DeviceId { get; set; } = null!;
        public bool IsOnline { get; set; }
    }
}
