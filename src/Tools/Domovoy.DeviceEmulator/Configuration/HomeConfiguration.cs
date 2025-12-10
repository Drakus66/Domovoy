using System.Text.Json.Serialization;

namespace Domovoy.DeviceEmulator.Configuration;

public class HomeConfiguration
{
    [JsonPropertyName("home_name")]
    public string HomeName { get; set; } = "Virtual Home";

    [JsonPropertyName("mqtt_broker")]
    public string MqttBroker { get; set; } = "localhost:1883";

    [JsonPropertyName("devices")]
    public List<DeviceConfiguration> Devices { get; set; } = new();
}

public class DeviceConfiguration
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("sensor_type")]
    public string SensorType { get; set; } = "multiset";
    
    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("update_interval_seconds")]
    public int UpdateIntervalSeconds { get; set; } = 30;
}
