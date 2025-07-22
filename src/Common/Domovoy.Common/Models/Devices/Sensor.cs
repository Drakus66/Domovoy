using Domovoy.Common.Models.Enums.EntityTypes;

namespace Domovoy.Common.Models.Devices
{
    public class Sensor : BaseEntity
    {
        public SensorTypes Type { get; set; }
        public SensorState State { get; set; } = new();
    }


    public class SensorState
    {
        public double? Temperature { get; set; } // Celsius
        public double? Humidity { get; set; } // Percentage
        public bool? MotionDetected { get; set; }
        public double? LightLevel { get; set; } // Lux
        public double? AirQuality { get; set; } // AQI
        public double? Pressure { get; set; } // hPa
        public double? BatteryLevel { get; set; } // Percentage
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}