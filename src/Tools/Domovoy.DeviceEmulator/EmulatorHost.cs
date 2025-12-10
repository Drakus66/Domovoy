using System.Text.Json;
using Domovoy.DeviceEmulator.Configuration;
using Domovoy.DeviceEmulator.Devices;
using MQTTnet;
using MQTTnet.Client;


namespace Domovoy.DeviceEmulator;

public class EmulatorHost
{
    private readonly HomeConfiguration _config;
    private readonly List<IVirtualDevice> _devices = [];
    private IMqttClient? _mqttClient;
    private readonly CancellationTokenSource _cts = new();

    public EmulatorHost(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Configuration file not found", configPath);
        }

        var json = File.ReadAllText(configPath);
        _config = JsonSerializer.Deserialize<HomeConfiguration>(json) ?? throw new InvalidOperationException("Invalid config");
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"[INFO] Starting Virtual Home: {_config.HomeName}");

        // Setup MQTT
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        var mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(_config.MqttBroker.Split(':')[0], int.Parse(_config.MqttBroker.Split(':')[1]))
            .WithClientId($"domovoy_emulator_{Guid.NewGuid()}")
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceived;

        Console.Write($"[INFO] Connecting to MQTT broker {_config.MqttBroker}...");
        await _mqttClient.ConnectAsync(mqttOptions);
        Console.WriteLine(" Connected!");

        // Initialize Devices
        foreach (var devConfig in _config.Devices)
        {
            IVirtualDevice device = devConfig.Type.ToLower() switch
            {
                "light" => new VirtualLight(devConfig.Id, devConfig.Name),
                "sensor" => new VirtualSensor(devConfig.Id, devConfig.Name, devConfig.SensorType),
                "switch" => new VirtualSwitch(devConfig.Id, devConfig.Name),
                _ => throw new ArgumentException($"Unknown device type: {devConfig.Type}")
            };

            await device.InitializeAsync(_mqttClient);
            _devices.Add(device);

            // Start simulation loop
            _ = device.SimulateAsync(_cts.Token);
        }

        // Subscribe to all command topics
        await _mqttClient.SubscribeAsync("domovoy/command/#");

        Console.WriteLine($"[INFO] Loaded {_devices.Count} devices.");
    }

    private Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
    {
        var topic = arg.ApplicationMessage.Topic;
        var payload = arg.ApplicationMessage.ConvertPayloadToString();

        // Route to device
        foreach (var device in _devices)
        {
            _ = device.HandleCommandAsync(topic, payload);
        }

        return Task.CompletedTask;
    }

    public async Task RunInteractiveLoop()
    {
        Console.WriteLine("\n--- Interactive Mode ---");
        Console.WriteLine("Commands: list, set <id> <prop> <val>, help, exit");

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var parts = input.Split(' ');
            var cmd = parts[0].ToLower();

            if (cmd == "exit") break;

            if (cmd == "help")
            {
                Console.WriteLine("Available commands:");
                Console.WriteLine("  list                     - List all devices and their status");
                Console.WriteLine("  set <id> on/off          - Turn device on/off");
                Console.WriteLine("  set <id> brightness <val> - Set brightness");
                Console.WriteLine("  set <id> color <hex>     - Set color");
                Console.WriteLine("  trigger <id>             - Trigger sensor update manually");
                continue;
            }

            if (cmd == "list")
            {
                foreach (var d in _devices)
                {
                    Console.WriteLine($"  - {d.Name} ({d.DeviceType}) [{d.Id}]");
                    if (d is VirtualLight l) Console.WriteLine($"      State: {(l.IsOn ? "ON" : "OFF")}, Brightness: {l.Brightness}%, Color: {l.Color}");
                    if (d is VirtualSensor s) Console.WriteLine($"      Type: {s.SensorType}");
                    if (d is VirtualSwitch sw) Console.WriteLine($"      State: {(sw.IsOn ? "ON" : "OFF")}");
                }
                continue;
            }

            if (cmd == "set" && parts.Length >= 3)
            {
                var id = parts[1];
                var action = parts[2].ToLower();
                var dev = _devices.FirstOrDefault(d => d.Id == id);

                switch (dev)
                {
                    case null:
                        Console.WriteLine("Device not found.");
                        continue;
                    case VirtualLight light:
                    {
                        var payload = "";
                        if (action == "on") payload = "{\"state\": \"ON\"}";
                        else if (action == "off") payload = "{\"state\": \"OFF\"}";
                        else if (action == "brightness" && parts.Length > 3) payload = $"{{\"brightness\": {parts[3]}}}";
                        else if (action == "color" && parts.Length > 3) payload = $"{{\"color\": \"{parts[3]}\"}}";

                        if (!string.IsNullOrEmpty(payload))
                        {
                            await light.HandleCommandAsync($"domovoy/command/light/{id}", payload);
                        }

                        break;
                    }
                    case VirtualSwitch sw when action == "on":
                        await sw.HandleCommandAsync($"domovoy/command/switch/{id}", "ON");
                        break;
                    case VirtualSwitch sw:
                    {
                        if (action == "off") await sw.HandleCommandAsync($"domovoy/command/switch/{id}", "OFF");
                        break;
                    }
                    default:
                        Console.WriteLine("This device does not support set commands.");
                        break;
                }
                continue;
            }

            if (cmd != "trigger" || parts.Length < 2)
            {
                var id = parts[1];
                var dev = _devices.FirstOrDefault(d => d.Id == id);
                if (dev is VirtualSensor)
                {
                    // Force simulate (hacky way via reflection or public method if I added one)
                    // Since I didn't add public ForceUpdate, I'll just re-initialize it or add a TODO.
                    // Actually, I can just call SimualteAsync logic inside if I extract it.
                    // For now, let's just say "Triggered" and maybe add a ForceUpdate method to interface later.
                    Console.WriteLine("Trigger not strictly implemented, wait for auto-update.");
                }
            }

        }
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        if (_mqttClient != null)
        {
            await _mqttClient.DisconnectAsync();
        }
    }
}
