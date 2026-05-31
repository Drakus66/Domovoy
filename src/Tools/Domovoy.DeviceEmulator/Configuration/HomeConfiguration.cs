using System.Text.Json.Serialization;

namespace Domovoy.DeviceEmulator.Configuration;

/// <summary>Emulated home: MQTT broker, the web-UI port and the set of virtual devices.</summary>
public class HomeConfiguration
{
    [JsonPropertyName("home_name")]
    public string HomeName { get; set; } = "Virtual Home";

    [JsonPropertyName("mqtt_broker")]
    public string MqttBroker { get; set; } = "localhost:1883";

    [JsonPropertyName("mqtt_username")]
    public string? MqttUsername { get; set; } = "user";

    [JsonPropertyName("mqtt_password")]
    public string? MqttPassword { get; set; } = "user";

    [JsonPropertyName("web_port")]
    public int WebPort { get; set; } = 5080;

    [JsonPropertyName("devices")]
    public List<DeviceConfiguration> Devices { get; set; } = new();
}

/// <summary>A virtual device declared by its capabilities (capability-native, like real Domovoy devices).</summary>
public class DeviceConfiguration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>If true, read-only numeric capabilities drift randomly to mimic a live sensor.</summary>
    [JsonPropertyName("simulate")]
    public bool Simulate { get; set; }

    [JsonPropertyName("capabilities")]
    public List<CapabilityConfiguration> Capabilities { get; set; } = new();
}

/// <summary>Declares one capability of a virtual device.</summary>
public class CapabilityConfiguration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Boolean | Number | Enum | Color | Text | Action.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Number";

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    /// <summary>Whether the server may command this capability (actuator). Sensors are read-only.</summary>
    [JsonPropertyName("writable")]
    public bool Writable { get; set; }
}
