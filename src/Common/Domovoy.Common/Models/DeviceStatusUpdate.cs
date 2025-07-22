using Domovoy.Common.Models;
using System;

namespace Domovoy.Common.Models
{
    /// <summary>
    /// Entity class for device status updates
    /// </summary>
    public class DeviceStatusUpdate : BaseEntity
    {
        public string DeviceId { get; set; } = null!;
        public new bool IsOnline { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
