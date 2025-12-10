namespace Domovoy.Common.Models
{
    /// <summary>
    /// Entity class for device state updates
    /// </summary>
    public class DeviceStateUpdate : BaseEntity
    {
        public string DeviceId { get; set; } = null!;
        public Dictionary<string, object> State { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
