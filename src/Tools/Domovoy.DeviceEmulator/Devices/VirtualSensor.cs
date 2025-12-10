using System.Text.Json;
using MQTTnet;

namespace Domovoy.DeviceEmulator.Devices;

public class VirtualSensor : IVirtualDevice
{
    private MQTTnet.Client.IMqttClient? _mqttClient;
    private readonly Random _random = new();

    public string Id { get; }
    public string Name { get; }
    public string DeviceType => "sensor";
    public string SensorType { get; } // temperature, humidity, multiset

    public VirtualSensor(string id, string name, string sensorType = "multiset")
    {
        Id = id;
        Name = name;
        SensorType = sensorType;
    }

    public async Task InitializeAsync(MQTTnet.Client.IMqttClient mqttClient)
    {
        _mqttClient = mqttClient;
        await AnnounceDiscoveryAsync();
        await PublishStateAsync();
    }

    public async Task SimulateAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Simulate periodic updates
            var delay = _random.Next(10000, 30000); // 10-30 seconds
            await Task.Delay(delay, cancellationToken);
            await PublishStateAsync();
        }
    }

    public Task HandleCommandAsync(string topic, string payload)
    {
        // Sensors usually don't handle commands
        return Task.CompletedTask;
    }

    private async Task PublishStateAsync()
    {
        if (_mqttClient == null) return;

        object state;

        if (SensorType == "temperature")
        {
            state = new { temperature = Math.Round(20 + (_random.NextDouble() * 5), 1) };
        }
        else if (SensorType == "humidity")
        {
            state = new { humidity = _random.Next(30, 70) };
        }
        else if (SensorType == "motion")
        {
            state = new { motion = _random.Next(0, 2) == 1 };
        }
        else
        {
            // Multiset
            state = new
            {
                temperature = Math.Round(20 + (_random.NextDouble() * 5), 1),
                humidity = _random.Next(30, 70),
                battery = _random.Next(50, 100)
            };
        }

        var payload = JsonSerializer.Serialize(state);
        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"domovoy/state/sensor/{Id}")
            .WithPayload(payload)
            .Build());

        Console.WriteLine($"[Sensor {Name}] Reported: {payload}");
    }

    private async Task AnnounceDiscoveryAsync()
    {
        if (_mqttClient == null) return;

        var config = new
        {
            name = Name,
            unique_id = Id,
            state_topic = $"domovoy/state/sensor/{Id}",
            device_class = (SensorType == "multiset" || SensorType == "motion") ? null : SensorType,
            // Simple templates for discovery
            value_template = SensorType == "temperature" ? "{{ value_json.temperature }}" :
                             SensorType == "humidity" ? "{{ value_json.humidity }}" : null,
            unit_of_measurement = SensorType == "temperature" ? "°C" :
                                  SensorType == "humidity" ? "%" : null,
            device = new
            {
                identifiers = new[] { Id },
                name = Name,
                model = "Virtual Sensor v1",
                manufacturer = "Domovoy Emulator"
            }
        };

        if (SensorType == "motion")
        {
            // Motion binary sensor might need different config/topic in HA, but for Domovoy Generic Sensor it might be just a value.
            // Let's keep it simple.
        }

        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"homeassistant/sensor/{Id}/config")
            .WithPayload(JsonSerializer.Serialize(config))
            .WithRetainFlag()
            .Build());

        Console.WriteLine($"[Sensor {Name}] Announced via discovery");
    }
}
