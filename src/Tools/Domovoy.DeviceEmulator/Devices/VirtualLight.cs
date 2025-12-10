using System.Text.Json;
using MQTTnet;

namespace Domovoy.DeviceEmulator.Devices;

public class VirtualLight : IVirtualDevice
{
    private MQTTnet.Client.IMqttClient? _mqttClient;
    private readonly Random _random = new();

    public string Id { get; }
    public string Name { get; }
    public string DeviceType => "light";

    // State
    public bool IsOn { get; private set; }
    public int Brightness { get; private set; }
    public string Color { get; private set; } = "#FFFFFF";

    public VirtualLight(string id, string name)
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
            // Lights are reactive, but we can simulate occasional updates or heartbeat
            await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            await PublishStateAsync();
        }
    }

    public async Task HandleCommandAsync(string topic, string payload)
    {
        if (!topic.Contains(Id)) return;

        Console.WriteLine($"[Light {Name}] Received command: {payload}");

        try
        {
            // Simple JSON parsing
            // Check for direct keywords first if not JSON
            if (payload.Trim().StartsWith("{"))
            {
                var command = JsonSerializer.Deserialize<JsonElement>(payload);

                if (command.TryGetProperty("state", out var stateProp))
                {
                    var state = stateProp.GetString()?.ToUpper();
                    if (state == "ON") IsOn = true;
                    if (state == "OFF") IsOn = false;
                }

                if (command.TryGetProperty("brightness", out var brightnessProp))
                {
                    Brightness = brightnessProp.GetInt32();
                    IsOn = true; // Auto-on
                }

                if (command.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.String)
                {
                    Color = colorProp.GetString() ?? "#FFFFFF";
                    IsOn = true;
                }
            }
            else
            {
                // Handle simple ON/OFF payloads if any
                if (payload == "ON") IsOn = true;
                if (payload == "OFF") IsOn = false;
            }

            // Acknowledge by publishing new state
            await PublishStateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to parse command for {Name}: {ex.Message}");
        }
    }

    private async Task PublishStateAsync()
    {
        if (_mqttClient == null) return;

        var state = new
        {
            state = IsOn ? "ON" : "OFF",
            brightness = Brightness,
            color = Color
        };

        var payload = JsonSerializer.Serialize(state);
        // Using domovoy specific topic for state
        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"domovoy/state/light/{Id}")
            .WithPayload(payload)
            .WithRetainFlag()
            .Build());

        Console.WriteLine($"[Light {Name}] Published state: {payload}");
    }

    private async Task AnnounceDiscoveryAsync()
    {
        if (_mqttClient == null) return;

        var config = new
        {
            name = Name,
            unique_id = Id,
            command_topic = $"domovoy/command/light/{Id}",
            state_topic = $"domovoy/state/light/{Id}",
            schema = "json",
            brightness = true,
            rgb = true,
            device = new
            {
                identifiers = new[] { Id },
                name = Name,
                model = "Virtual Light v1",
                manufacturer = "Domovoy Emulator"
            }
        };

        await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"homeassistant/light/{Id}/config")
            .WithPayload(JsonSerializer.Serialize(config))
            .WithRetainFlag()
            .Build());

        Console.WriteLine($"[Light {Name}] Announced via discovery");
    }
}
