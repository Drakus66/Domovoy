using System.Text.Json;
using MQTTnet;

namespace Domovoy.DeviceEmulator.Devices;

public class VirtualSwitch : IVirtualDevice
{
    private MQTTnet.Client.IMqttClient? _mqttClient;

    public string Id { get; }
    public string Name { get; }
    public string DeviceType => "switch";

    public bool IsOn { get; private set; }

    public VirtualSwitch(string id, string name)
    {
        Id = id;
        Name = name;
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
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            await PublishStateAsync(); // Heartbeat
        }
    }

    public async Task HandleCommandAsync(string topic, string payload)
    {
        if (!topic.Contains(Id)) return;

        Console.WriteLine($"[Switch {Name}] Received command: {payload}");

        if (payload.Contains("ON", StringComparison.OrdinalIgnoreCase)) IsOn = true;
        if (payload.Contains("OFF", StringComparison.OrdinalIgnoreCase)) IsOn = false;

        await PublishStateAsync();
    }

    private async Task PublishStateAsync()
    {
        if (_mqttClient == null) return;

        var payload = IsOn ? "ON" : "OFF";
        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"domovoy/state/switch/{Id}")
            .WithPayload(payload)
            .WithRetainFlag()
            .Build());

        Console.WriteLine($"[Switch {Name}] State: {payload}");
    }

    private async Task AnnounceDiscoveryAsync()
    {
        if (_mqttClient == null) return;

        var config = new
        {
            name = Name,
            unique_id = Id,
            command_topic = $"domovoy/command/switch/{Id}",
            state_topic = $"domovoy/state/switch/{Id}",
            device = new
            {
                identifiers = new[] { Id },
                name = Name,
                model = "Virtual Switch v1",
                manufacturer = "Domovoy Emulator"
            }
        };

        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"homeassistant/switch/{Id}/config")
            .WithPayload(JsonSerializer.Serialize(config))
            .WithRetainFlag()
            .Build());

        Console.WriteLine($"[Switch {Name}] Announced via discovery");
    }
}
