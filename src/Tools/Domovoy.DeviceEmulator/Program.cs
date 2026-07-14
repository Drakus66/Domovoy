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
        {
          "id": "living_room_light", "name": "Living Room Light", "model": "Virtual Dimmable Light",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true },
            { "id": "brightness", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true }
          ]
        },
        {
          "id": "kitchen_climate", "name": "Kitchen Climate", "model": "Virtual Multi-Sensor", "simulate": true,
          "capabilities": [
            { "id": "temperature", "kind": "Number", "unit": "°C", "min": 15, "max": 30 },
            { "id": "humidity", "kind": "Number", "unit": "%", "min": 30, "max": 70 },
            { "id": "co2", "kind": "Number", "unit": "ppm", "min": 400, "max": 1500 }
          ]
        },
        {
          "id": "hallway_motion", "name": "Hallway Motion", "model": "Virtual Motion Sensor",
          "capabilities": [ { "id": "occupancy", "kind": "Boolean" } ]
        },
        {
          "id": "garage_relay", "name": "Garage Relay", "model": "Virtual Switch",
          "capabilities": [ { "id": "on_off", "kind": "Boolean", "writable": true } ]
        },

        { "id": "outdoor", "name": "Outdoor", "model": "Virtual Weather Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": -25, "max": 40 } ] },

        { "id": "living_convector", "name": "Living Room Convector", "model": "Virtual Convector",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "living_temp", "name": "Living Room Temperature", "model": "Virtual Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] },

        { "id": "bedroom_convector", "name": "Bedroom Convector", "model": "Virtual Convector",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "bedroom_temp", "name": "Bedroom Temperature", "model": "Virtual Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] },

        { "id": "office_convector", "name": "Office Convector", "model": "Virtual Convector",
          "capabilities": [
            { "id": "on_off", "kind": "Boolean", "writable": true, "default": true },
            { "id": "power", "kind": "Number", "unit": "%", "min": 0, "max": 100, "writable": true, "default": 100 }
          ] },
        { "id": "office_temp", "name": "Office Temperature", "model": "Virtual Temperature Sensor",
          "capabilities": [ { "id": "temperature", "kind": "Number", "unit": "°C", "min": 0, "max": 40 } ] }
      ],
      "external_temperature": {
        "device_id": "outdoor", "capability": "temperature",
        "initial": 5, "drift_amplitude": 2, "drift_period_minutes": 20
      },
      "thermal_zones": [
        { "name": "Living Room", "heater_device": "living_convector", "sensor_device": "living_temp",
          "heat_gain_at_full": 3.0, "ambient_coupling": 0.12, "initial_temp": 18 },
        { "name": "Bedroom", "heater_device": "bedroom_convector", "sensor_device": "bedroom_temp",
          "heat_gain_at_full": 2.4, "ambient_coupling": 0.10, "initial_temp": 17 },
        { "name": "Office", "heater_device": "office_convector", "sensor_device": "office_temp",
          "heat_gain_at_full": 4.0, "ambient_coupling": 0.16, "initial_temp": 19 }
      ]
    }
    """;
}
