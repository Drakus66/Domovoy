using Domovoy.DeviceEmulator;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run -- --config <path_to_config.json>");
    // Default fallback for easier debugging
    // args = new[] { "--config", "VirtualHome.json" };
}

var configPath = "VirtualHome.json"; // Default
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--config" && i + 1 < args.Length)
    {
        configPath = args[i + 1];
    }
}

if (!File.Exists(configPath))
{
    // Create default if not exists
    Console.WriteLine("Config not found, creating default VirtualHome.json...");
    var defaultConfig = @"{
  ""home_name"": ""Demo Home"",
  ""mqtt_broker"": ""localhost:1883"",
  ""devices"": [
    {
      ""id"": ""living_room_light"",
      ""type"": ""light"",
      ""name"": ""Living Room Main Light"",
      ""capabilities"": [""brightness"", ""color""]
    },
    {
      ""id"": ""kitchen_sensor"",
      ""type"": ""sensor"",
      ""name"": ""Kitchen Environment"",
      ""sensor_type"": ""multiset"",
      ""update_interval_seconds"": 10
    },
    {
      ""id"": ""hallway_switch"",
      ""type"": ""switch"",
      ""name"": ""Hallway Light Switch""
    }
  ]
}";
    await File.WriteAllTextAsync(configPath, defaultConfig);
}

try
{
    var host = new EmulatorHost(configPath);
    await host.StartAsync();
    await host.RunInteractiveLoop();
    await host.StopAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Critical Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
