using System.Text.Json.Serialization;

namespace Domovoy.Common.Models.Devices
{
    public class Light : BaseEntity
    {
        public Light()
        {
            State = new LightState();
            Name = string.Empty;
        }
        public LightState State { get; set; }

        [JsonIgnore] public bool IsOn => State.IsOn;

        [JsonIgnore] public int Brightness => State.Brightness;
    }

    public class LightState
    {
        public bool IsOn { get; set; }
        public int Brightness { get; set; } = 100; // 0-100%
        public string Color { get; set; } = "#FFFFFF"; // Hex color code
        public int ColorTemperature { get; set; } = 4000; // Kelvin
    }
}
