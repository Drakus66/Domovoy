// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

using Domovoy.DeviceEmulator;
using Domovoy.DeviceEmulator.Configuration;

// ---- Resolve / create config -------------------------------------------------
var configPath = "VirtualHome.json";
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--config" && i + 1 < args.Length)
        configPath = args[i + 1];

if (!File.Exists(configPath))
{
    await File.WriteAllTextAsync(configPath, DefaultConfig.Json);
    Console.WriteLine($"[INFO] Created default config at {configPath}");
}

var config = JsonSerializer.Deserialize<HomeConfiguration>(await File.ReadAllTextAsync(configPath))
             ?? throw new InvalidOperationException("Invalid configuration");

// ---- Web host ----------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://localhost:{config.WebPort}");
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<EmulatorEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmulatorEngine>());

var app = builder.Build();
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

// Current devices + their state.
app.MapGet("/api/devices", (EmulatorEngine engine) => Results.Json(engine.Snapshot(), jsonOptions));

// Set a capability value (UI = the physical world: set a sensor reading or override an actuator).
app.MapPost("/api/devices/{id}/set", async (string id, SetRequest req, EmulatorEngine engine) =>
    await engine.SetValueAsync(id, req.Capability, req.Value, "ui") ? Results.Ok() : Results.NotFound());

// Announce the device to the server (re-trigger discovery) / remove it from the server.
app.MapPost("/api/devices/{id}/register", async (string id, EmulatorEngine engine) =>
    await engine.RegisterAsync(id) ? Results.Ok() : Results.NotFound());

app.MapPost("/api/devices/{id}/unregister", async (string id, EmulatorEngine engine) =>
    await engine.UnregisterAsync(id) ? Results.Ok() : Results.NotFound());

// Live updates (snapshot on connect, then state/log events).
app.Map("/ws", async (HttpContext ctx, EmulatorEngine engine) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    var channel = Channel.CreateUnbounded<string>();
    void OnUpdate(EmulatorUpdate u) => channel.Writer.TryWrite(JsonSerializer.Serialize(u, jsonOptions));
    engine.Updated += OnUpdate;

    try
    {
        channel.Writer.TryWrite(JsonSerializer.Serialize(
            new EmulatorUpdate("snapshot", null, engine.Snapshot()), jsonOptions));

        await foreach (var msg in channel.Reader.ReadAllAsync(ctx.RequestAborted))
            await socket.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ctx.RequestAborted);
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    finally { engine.Updated -= OnUpdate; }
});

Console.WriteLine($"[INFO] Emulator UI:  http://localhost:{config.WebPort}");
app.Run();

internal record SetRequest(string Capability, JsonElement Value);

internal static class DefaultConfig
{
    public const string Json = """
    {
      "home_name": "Domovoy Test Home",
      "mqtt_broker": "localhost:1883",
      "mqtt_username": "user",
      "mqtt_password": "user",
      "web_port": 5080,
      "devices": [
        { "id": "living_convector", "name": "Гостиная — Конвектор 1500 Вт", "model": "Convector 1500W",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "living_temp", "name": "Гостиная — Датчик температуры", "model": "Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] },
        { "id": "living_co2", "name": "Гостиная — Датчик CO₂", "model": "CO2 Sensor",
          "capabilities": [ { "id": "co2", "kind": "Number", "unit": "ppm", "min": 400, "max": 2000 } ] },
        { "id": "living_light", "name": "Гостиная — Светильник RGB", "model": "RGBW Light",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true },
            { "id": "brightness", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true },
            { "id": "color_temp", "kind": "Number", "unit": "K", "min": 2200, "max": 6500, "writable": true },
            { "id": "color", "kind": "Color", "writable": true }
          ] },

        { "id": "bedroom_convector", "name": "Спальня — Конвектор 1000 Вт", "model": "Convector 1000W",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "bedroom_temp", "name": "Спальня — Датчик температуры", "model": "Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] },
        { "id": "bedroom_co2", "name": "Спальня — Датчик CO₂", "model": "CO2 Sensor",
          "capabilities": [ { "id": "co2", "kind": "Number", "unit": "ppm", "min": 400, "max": 2000 } ] },
        { "id": "bedroom_light", "name": "Спальня — Светильник RGB", "model": "RGBW Light",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true },
            { "id": "brightness", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true },
            { "id": "color_temp", "kind": "Number", "unit": "K", "min": 2200, "max": 6500, "writable": true },
            { "id": "color", "kind": "Color", "writable": true }
          ] },

        { "id": "bathroom_convector", "name": "Санузел — Конвектор 900 Вт", "model": "Convector 900W",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "bathroom_temp", "name": "Санузел — Датчик температуры", "model": "Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] },
        { "id": "bathroom_co2", "name": "Санузел — Датчик CO₂", "model": "CO2 Sensor",
          "capabilities": [ { "id": "co2", "kind": "Number", "unit": "ppm", "min": 400, "max": 2000 } ] },
        { "id": "bathroom_light", "name": "Санузел — Светильник", "model": "Dimmable Light",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true },
            { "id": "brightness", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true }
          ] },
        { "id": "bathroom_presence", "name": "Санузел — Датчик присутствия", "model": "Presence Sensor",
          "capabilities": [ { "id": "occupancy", "kind": "Boolean", "writable": false } ] },
        { "id": "bathroom_pressure", "name": "Санузел — Датчик давления воды", "model": "Water Pressure Sensor",
          "capabilities": [ { "id": "pressure", "kind": "Number", "unit": "атм", "min": 1, "max": 10 } ] },
        { "id": "bathroom_pump", "name": "Санузел — Реле насоса", "model": "Pump Relay",
          "capabilities": [ { "id": "on_off", "kind": "Boolean", "writable": true, "default": false } ] },

        { "id": "kitchen_convector", "name": "Кухня — Конвектор 900 Вт", "model": "Convector 900W",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "kitchen_temp", "name": "Кухня — Датчик температуры", "model": "Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] },
        { "id": "kitchen_co2", "name": "Кухня — Датчик CO₂", "model": "CO2 Sensor",
          "capabilities": [ { "id": "co2", "kind": "Number", "unit": "ppm", "min": 400, "max": 2000 } ] },
        { "id": "kitchen_light", "name": "Кухня — Светильник", "model": "Light",
          "capabilities": [ { "id": "on_off", "kind": "Boolean", "writable": true } ] },
        { "id": "kitchen_led", "name": "Кухня — LED-лента", "model": "LED Strip",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true },
            { "id": "brightness", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true }
          ] },

        { "id": "ventilation", "name": "Приточно-вытяжная вентиляция", "model": "Supply-Exhaust Fan",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "fan_speed", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 50 }
          ] },
        { "id": "outdoor", "name": "Улица — Температура", "model": "Weather Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": -30, "max": 45 } ] }
      ],
      "external_temperature": {
        "device_id": "outdoor", "capability": "temperature",
        "initial": 5, "drift_amplitude": 2, "drift_period_minutes": 20
      },
      "thermal_zones": [
        { "name": "Гостиная", "heater_device": "living_convector", "sensor_device": "living_temp",
          "heat_gain_at_full": 3.5, "ambient_coupling": 0.12, "initial_temp": 18 },
        { "name": "Спальня", "heater_device": "bedroom_convector", "sensor_device": "bedroom_temp",
          "heat_gain_at_full": 2.4, "ambient_coupling": 0.11, "initial_temp": 17 },
        { "name": "Санузел", "heater_device": "bathroom_convector", "sensor_device": "bathroom_temp",
          "heat_gain_at_full": 2.1, "ambient_coupling": 0.14, "initial_temp": 19 },
        { "name": "Кухня", "heater_device": "kitchen_convector", "sensor_device": "kitchen_temp",
          "heat_gain_at_full": 2.1, "ambient_coupling": 0.12, "initial_temp": 18 }
      ],
      "co2_zones": [
        { "name": "Гостиная", "vent_device": "ventilation", "sensor_device": "living_co2",
          "generation": 15, "equilibrium_fraction": 0.5, "initial": 700 },
        { "name": "Спальня", "vent_device": "ventilation", "sensor_device": "bedroom_co2",
          "generation": 15, "equilibrium_fraction": 0.5, "initial": 750 },
        { "name": "Кухня", "vent_device": "ventilation", "sensor_device": "kitchen_co2",
          "generation": 16, "equilibrium_fraction": 0.5, "initial": 800 },
        { "name": "Санузел", "vent_device": "ventilation", "sensor_device": "bathroom_co2",
          "generation": 15, "equilibrium_fraction": 0.5, "initial": 450,
          "presence_device": "bathroom_presence", "presence_capability": "occupancy", "idle_decay": 0.3 }
      ],
      "pressure_zones": [
        { "name": "Санузел", "pump_device": "bathroom_pump", "sensor_device": "bathroom_pressure",
          "fill_rate": 0.6, "drain_rate": 0.12, "initial": 4, "min": 1, "max": 10 }
      ],
      "power_meters": [
        { "device_id": "living_convector",  "rated_watts": 1500, "level_capability": "power", "expose_power": false },
        { "device_id": "bedroom_convector",  "rated_watts": 1000, "level_capability": "power", "expose_power": false },
        { "device_id": "bathroom_convector", "rated_watts": 900,  "level_capability": "power", "expose_power": false },
        { "device_id": "kitchen_convector",  "rated_watts": 900,  "level_capability": "power", "expose_power": false },
        { "device_id": "living_light",  "rated_watts": 12, "level_capability": "brightness", "noise_watts": 0.2 },
        { "device_id": "bedroom_light", "rated_watts": 12, "level_capability": "brightness", "noise_watts": 0.2 },
        { "device_id": "bathroom_light","rated_watts": 8,  "level_capability": "brightness", "noise_watts": 0.2 },
        { "device_id": "kitchen_light", "rated_watts": 15, "noise_watts": 0.2 },
        { "device_id": "kitchen_led",   "rated_watts": 24, "level_capability": "brightness", "noise_watts": 0.3 },
        { "device_id": "ventilation",   "rated_watts": 60, "level_capability": "fan_speed", "noise_watts": 0.5 },
        { "device_id": "bathroom_pump", "rated_watts": 500, "noise_watts": 3 }
      ]
    }
    """;
}
