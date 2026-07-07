using System.Text.Json;
using System.Text.Json.Serialization;

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Native;
using Domovoy.DeviceEmulator.Configuration;
using Domovoy.DeviceEmulator.Devices;

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace Domovoy.DeviceEmulator;

/// <summary>A live message pushed to the web UI over the WebSocket.</summary>
/// <param name="Kind">"snapshot" | "state" | "log".</param>
public sealed record EmulatorUpdate(string Kind, string? DeviceId, object? Data);

public sealed record CapabilityView(string Id, string Kind, string? Unit, double? Min, double? Max, bool Writable);
public sealed record DeviceView(string Id, string Name, string? Model, bool Registered, IReadOnlyList<CapabilityView> Capabilities, IReadOnlyDictionary<string, object?> State);

/// <summary>
/// Reference implementation + test harness of the Domovoy Native protocol. Owns one MQTT connection,
/// announces virtual devices, applies server <c>/set</c> commands and lets the UI set capability
/// values (simulating the physical world). Every change is published to MQTT and pushed to the UI.
/// </summary>
public sealed class EmulatorEngine : IHostedService
{
    private static readonly JsonSerializerOptions AnnounceOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HomeConfiguration _config;
    private readonly ILogger<EmulatorEngine> _logger;
    private readonly List<VirtualDevice> _devices;
    private readonly CancellationTokenSource _cts = new();
    private IMqttClient? _client;

    /// <summary>
    /// Stable board ("hub") id for this emulator process. The emulator is a single MQTT connection
    /// fronting many virtual devices — exactly the "board" the native protocol models — so it announces
    /// every device under this hub and wills a single <see cref="NativeProtocol.HubStatusTopic"/> offline.
    /// If the process dies ungracefully the broker fires that will, and the server fans it out to offline
    /// every device this emulator fronts (see <c>DomovoyNativeAdapter.HandleHubStatusAsync</c>).
    /// </summary>
    private readonly string _hubId;

    /// <summary>How many seconds one simulation tick advances the physics — must match the loop delay below.</summary>
    private const double TickSeconds = 15;

    private readonly Random _rng = new();

    // ---- Thermal simulation state ----------------------------------------
    private readonly ExternalTemperatureConfiguration? _externalConfig;
    private readonly VirtualDevice? _externalDevice;
    /// <summary>Centre the outdoor drift oscillates around; re-set when the user drags the outdoor slider.</summary>
    private double _externalCenter;
    private double _externalTemp;
    private readonly List<ThermalZone> _thermalZones;

    /// <summary>One resolved heater→sensor coupling with its live room temperature.</summary>
    private sealed class ThermalZone
    {
        public required string Name { get; init; }
        public required VirtualDevice Heater { get; init; }
        public required string PowerCapability { get; init; }
        public required VirtualDevice Sensor { get; init; }
        public required string TemperatureCapability { get; init; }
        public required double HeatGainAtFull { get; init; }
        public required double AmbientCoupling { get; init; }
        public required double Noise { get; init; }
        public double Temp { get; set; }
    }

    /// <summary>Raised on any device state change / log line; the web layer forwards it to WebSocket clients.</summary>
    public event Action<EmulatorUpdate>? Updated;

    public EmulatorEngine(HomeConfiguration config, ILogger<EmulatorEngine> logger)
    {
        _config = config;
        _logger = logger;
        _devices = config.Devices.Select(d => new VirtualDevice(d)).ToList();
        _hubId = MakeHubId(config.HomeName);

        _externalConfig = config.ExternalTemperature;
        if (_externalConfig is not null)
        {
            _externalCenter = _externalConfig.Initial;
            _externalTemp = _externalConfig.Initial;
            _externalDevice = _devices.FirstOrDefault(d => d.Id == _externalConfig.DeviceId);
            if (_externalDevice is null)
                _logger.LogWarning("external_temperature references unknown device '{Id}'", _externalConfig.DeviceId);
            else
                _externalDevice.SetValue(_externalConfig.Capability, Math.Round(_externalTemp, 1));
        }

        _thermalZones = BuildThermalZones(config);
    }

    /// <summary>Resolves each configured zone to its heater/sensor devices and seeds the sensor with the start temp.</summary>
    private List<ThermalZone> BuildThermalZones(HomeConfiguration config)
    {
        var zones = new List<ThermalZone>();
        foreach (var z in config.ThermalZones)
        {
            var heater = _devices.FirstOrDefault(d => d.Id == z.HeaterDevice);
            var sensor = _devices.FirstOrDefault(d => d.Id == z.SensorDevice);
            if (heater is null || sensor is null)
            {
                _logger.LogWarning("thermal_zone '{Name}' references missing device(s) heater='{H}' sensor='{S}' — skipped",
                    z.Name, z.HeaterDevice, z.SensorDevice);
                continue;
            }
            sensor.SetValue(z.SensorCapability, Math.Round(z.InitialTemp, 1));
            zones.Add(new ThermalZone
            {
                Name = z.Name,
                Heater = heater,
                PowerCapability = z.HeaterCapability,
                Sensor = sensor,
                TemperatureCapability = z.SensorCapability,
                HeatGainAtFull = z.HeatGainAtFull,
                AmbientCoupling = z.AmbientCoupling,
                Noise = z.Noise,
                Temp = z.InitialTemp,
            });
        }
        return zones;
    }

    /// <summary>Turns the home name into a topic-safe hub id, e.g. "My Home" → "emulator-my-home".</summary>
    private static string MakeHubId(string? homeName)
    {
        var slug = new string((homeName ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray())
            .Trim('-');
        return string.IsNullOrEmpty(slug) ? "emulator" : $"emulator-{slug}";
    }

    // ---- IHostedService ---------------------------------------------------

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var parts = _config.MqttBroker.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 1883;

        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMqttMessageAsync;

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId($"domovoy_emulator_{Guid.NewGuid()}")
            // Board Last-Will: on an ungraceful drop (crash / kill / lost network) the broker publishes
            // hub=offline, which the server fans out to offline every device this emulator fronts — so the
            // dashboard stops showing dead virtual devices as reachable.
            .WithWillTopic(NativeProtocol.HubStatusTopic(_hubId))
            .WithWillPayload(NativeProtocol.Offline)
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        if (!string.IsNullOrEmpty(_config.MqttUsername))
            optionsBuilder.WithCredentials(_config.MqttUsername, _config.MqttPassword);

        var options = optionsBuilder.Build();

        try
        {
            await _client.ConnectAsync(options, cancellationToken);
            _logger.LogInformation("Emulator connected to MQTT {Host}:{Port} as user {User}", host, port, _config.MqttUsername);
            // Announce the board as reachable so a fresh subscriber sees the retained online, mirroring the
            // Last-Will offline the broker will publish if we drop.
            await PublishHubStatusAsync(online: true);
        }
        catch (Exception ex)
        {
            // Keep the web UI alive even if the broker is down/misconfigured — the host must not fail to start.
            _logger.LogError(ex, "Emulator could not connect to MQTT {Host}:{Port}; running UI-only without the bus.", host, port);
            return;
        }

        await _client.SubscribeAsync(
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter("domovoy/native/+/set")
                .WithTopicFilter(NativeProtocol.DiscoverTopic)
                .Build(),
            cancellationToken);

        foreach (var device in _devices)
            await RegisterAsync(device.Id);

        _ = SimulateLoopAsync(_cts.Token);
        _logger.LogInformation("Emulator started '{Home}' with {Count} virtual devices", _config.HomeName, _devices.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        if (_client is { IsConnected: true })
        {
            foreach (var device in _devices)
                await PublishAvailabilityAsync(device, online: false);
            await PublishHubStatusAsync(online: false);
            await _client.DisconnectAsync();
        }
    }

    // ---- Public API (used by the web layer) -------------------------------

    public IReadOnlyList<DeviceView> Snapshot() => _devices.Select(ToView).ToList();

    /// <summary>
    /// Announces the device to the server (retained announce + online availability + current state) and
    /// marks it registered. Re-announcing a live device is the reliable way to surface it: RabbitMQ's MQTT
    /// retained-message store is not delivered to the server's wildcard subscription, so a device that
    /// announced while Connectivity was down would otherwise never be discovered until the next restart.
    /// </summary>
    public async Task<bool> RegisterAsync(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return false;

        device.Registered = true;
        await AnnounceAsync(device);
        await PublishStateAsync(device);
        RaiseLog(device.Id, "ui", "registered → announced to server");
        RaiseRegistration(device);
        return true;
    }

    /// <summary>
    /// Removes the device from the server: publishes offline availability and clears the retained announce
    /// (empty retained payload), then marks it unregistered so the engine stops publishing its state. The
    /// device stays in the emulator UI so it can be re-registered to re-test discovery.
    /// </summary>
    public async Task<bool> UnregisterAsync(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return false;

        await PublishAvailabilityAsync(device, online: false);
        await ClearRetainedAnnounceAsync(device);
        device.Registered = false;
        RaiseLog(device.Id, "ui", "unregistered → removed from server");
        RaiseRegistration(device);
        return true;
    }

    /// <summary>Sets a capability value (from the UI = physical world), publishes state, notifies the UI.</summary>
    public async Task<bool> SetValueAsync(string deviceId, string capabilityId, object? rawValue, string source = "ui")
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null || !device.HasCapability(capabilityId)) return false;

        device.SetValue(capabilityId, Clamp(device, capabilityId, Normalize(rawValue)));

        // Dragging the outdoor sensor re-centres the drift, so the manual value holds instead of being
        // overwritten by the next physics tick.
        if (_externalConfig is not null && deviceId == _externalConfig.DeviceId
            && capabilityId == _externalConfig.Capability && TryToDouble(device.State[capabilityId], out var center))
            _externalCenter = center;

        await PublishStateAsync(device);
        RaiseLog(deviceId, source, $"{capabilityId} = {device.State[capabilityId]}");
        RaiseState(device);
        return true;
    }

    // ---- MQTT inbound (server commands) -----------------------------------

    private async Task OnMqttMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;

        // Server (re)started and asked everyone to re-announce — re-publish every registered device so
        // the adapter relearns its deviceId→hardwareId mapping and can route commands again.
        if (topic == NativeProtocol.DiscoverTopic)
        {
            foreach (var registered in _devices.Where(d => d.Registered))
            {
                await AnnounceAsync(registered);
                await PublishStateAsync(registered);
            }
            return;
        }

        if (!topic.EndsWith("/set", StringComparison.Ordinal)) return;

        var hwId = NativeProtocol.DeviceIdFromTopic(topic);
        var device = _devices.FirstOrDefault(d => d.Id == hwId);
        if (device is null) return;

        var payload = e.ApplicationMessage.ConvertPayloadToString();
        Dictionary<string, JsonElement>? set;
        try { set = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload); }
        catch (JsonException) { return; }
        if (set is null) return;

        foreach (var (capId, value) in set)
        {
            if (device.HasCapability(capId))
                device.SetValue(capId, Clamp(device, capId, Normalize(value)));
        }

        await PublishStateAsync(device);
        RaiseLog(device.Id, "server", $"command {payload}");
        RaiseState(device);
    }

    // ---- MQTT outbound ----------------------------------------------------

    private async Task AnnounceAsync(VirtualDevice device)
    {
        if (_client is null) return;

        var announce = new NativeAnnounceV1
        {
            DeviceId = device.Id,
            Name = device.Name,
            Model = device.Model,
            Firmware = "emulator-1.0",
            Hub = _hubId,
            Capabilities = device.Capabilities
        };

        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.AnnounceTopic(device.Id))
            .WithPayload(JsonSerializer.Serialize(announce, AnnounceOptions))
            .WithRetainFlag()
            .Build());

        await PublishAvailabilityAsync(device, online: true);
    }

    private async Task PublishStateAsync(VirtualDevice device)
    {
        // Skip the bus while unregistered: a stray state report would re-create the device server-side
        // (the persistence interceptor upserts on state), silently undoing the unregister.
        if (_client is null || !device.Registered) return;
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.StateTopic(device.Id))
            .WithPayload(JsonSerializer.Serialize(device.State))
            .Build());
    }

    /// <summary>Deletes the retained announce (zero-length retained payload) so a restarted server won't rediscover the device.</summary>
    private async Task ClearRetainedAnnounceAsync(VirtualDevice device)
    {
        if (_client is null) return;
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.AnnounceTopic(device.Id))
            .WithPayload(Array.Empty<byte>())
            .WithRetainFlag()
            .Build());
    }

    private async Task PublishAvailabilityAsync(VirtualDevice device, bool online)
    {
        if (_client is null) return;
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.AvailabilityTopic(device.Id))
            .WithPayload(online ? NativeProtocol.Online : NativeProtocol.Offline)
            .WithRetainFlag()
            .Build());
    }

    /// <summary>
    /// Publishes the emulator board's reachability to its hub-status topic (retained). This is the
    /// connection-level twin of the per-device availability and matches the board Last-Will the server
    /// uses to offline every device the emulator fronts when it drops.
    /// </summary>
    private async Task PublishHubStatusAsync(bool online)
    {
        if (_client is null) return;
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(NativeProtocol.HubStatusTopic(_hubId))
            .WithPayload(online ? NativeProtocol.Online : NativeProtocol.Offline)
            .WithRetainFlag()
            .Build());
    }

    // ---- Simulation -------------------------------------------------------

    private async Task SimulateLoopAsync(CancellationToken ct)
    {
        var rng = new Random();
        var tick = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                tick++;

                // Liveness heartbeat (~every 30s): re-assert availability=online for every registered
                // device so the server's staleness watchdog keeps them online. A device that never changes
                // state (e.g. a plain switch) would otherwise look silent and get expired while we run.
                if (tick % 2 == 0)
                    foreach (var device in _devices.Where(d => d.Registered))
                        await PublishAvailabilityAsync(device, online: true);

                await StepThermalAsync(tick);

                foreach (var device in _devices.Where(d => d.Simulate))
                {
                    var changed = false;
                    foreach (var cap in device.Capabilities.Where(IsReadableNumber))
                    {
                        device.SetValue(cap.Id, RandomInRange(cap, rng));
                        changed = true;
                    }
                    if (changed)
                    {
                        await PublishStateAsync(device);
                        RaiseState(device);
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private static bool IsReadableNumber(Capability cap) =>
        cap.Kind == CapabilityKind.Number && !cap.IsWritable;

    private static double RandomInRange(Capability cap, Random rng)
    {
        var min = cap.Attributes.TryGetValue(CapabilityAttributeKeys.Min, out var mn) && TryToDouble(mn, out var lo) ? lo : 0;
        var max = cap.Attributes.TryGetValue(CapabilityAttributeKeys.Max, out var mx) && TryToDouble(mx, out var hi) ? hi : min + 100;
        return Math.Round(min + rng.NextDouble() * (max - min), 1);
    }

    // ---- Thermal physics (heater ⇄ sensor coupling, hidden from the server) ----

    /// <summary>
    /// Advances the shared outdoor temperature and every zone's room temperature by one tick, then republishes the
    /// affected sensors. The heater→sensor coupling exists only here: the server sees an actuator (convector power)
    /// and a temperature sensor as unrelated devices, exactly the dependency a thermostat must be told about or the
    /// ML must discover.
    /// </summary>
    private async Task StepThermalAsync(int tick)
    {
        if (_externalConfig is null && _thermalZones.Count == 0) return;

        _externalTemp = ComputeExternalTemp(tick);
        if (_externalDevice is not null && _externalConfig is not null)
        {
            _externalDevice.SetValue(_externalConfig.Capability, Math.Round(_externalTemp, 1));
            await PublishStateAsync(_externalDevice);
            RaiseState(_externalDevice);
        }

        foreach (var zone in _thermalZones)
        {
            // Effective power: a convector with on_off=false contributes no heat regardless of the power dial, so a
            // bang-bang thermostat (drives on_off) and a proportional/ML controller (drives power) both work.
            var powered = !zone.Heater.HasCapability(CapabilityIds.OnOff)
                          || zone.Heater.State[CapabilityIds.OnOff] is not false;
            var power = powered && TryToDouble(zone.Heater.State.GetValueOrDefault(zone.PowerCapability), out var pw)
                ? Math.Clamp(pw / 100.0, 0, 1)
                : 0;

            var noise = (_rng.NextDouble() - 0.5) * 2 * zone.Noise;
            zone.Temp += zone.HeatGainAtFull * power - zone.AmbientCoupling * (zone.Temp - _externalTemp) + noise;

            zone.Sensor.SetValue(zone.TemperatureCapability, Math.Round(zone.Temp, 1));
            await PublishStateAsync(zone.Sensor);
            RaiseState(zone.Sensor);
        }
    }

    /// <summary>Outdoor temperature = manual centre + a slow sine wobble (compressed day/night).</summary>
    private double ComputeExternalTemp(int tick)
    {
        if (_externalConfig is null || _externalConfig.DriftAmplitude <= 0) return _externalCenter;
        var periodSeconds = Math.Max(1, _externalConfig.DriftPeriodMinutes) * 60.0;
        var phase = 2 * Math.PI * (tick * TickSeconds) / periodSeconds;
        return _externalCenter + _externalConfig.DriftAmplitude * Math.Sin(phase);
    }

    // ---- helpers ----------------------------------------------------------

    private void RaiseState(VirtualDevice device) =>
        Updated?.Invoke(new EmulatorUpdate("state", device.Id, device.State));

    private void RaiseRegistration(VirtualDevice device) =>
        Updated?.Invoke(new EmulatorUpdate("registration", device.Id, new { registered = device.Registered }));

    private void RaiseLog(string deviceId, string source, string text) =>
        Updated?.Invoke(new EmulatorUpdate("log", deviceId, new { source, text, ts = DateTime.Now.ToString("HH:mm:ss") }));

    private static DeviceView ToView(VirtualDevice d) => new(
        d.Id, d.Name, d.Model, d.Registered,
        d.Capabilities.Select(c => new CapabilityView(
            c.Id,
            c.Kind.ToString(),
            c.Attributes.TryGetValue(CapabilityAttributeKeys.Unit, out var u) ? u?.ToString() : null,
            c.Attributes.TryGetValue(CapabilityAttributeKeys.Min, out var mn) && TryToDouble(mn, out var lo) ? lo : null,
            c.Attributes.TryGetValue(CapabilityAttributeKeys.Max, out var mx) && TryToDouble(mx, out var hi) ? hi : null,
            c.IsWritable)).ToList(),
        d.State);

    private static object? Normalize(object? v) => v switch
    {
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => e.GetString(),
            _ => null
        },
        _ => v
    };

    private static object? Clamp(VirtualDevice device, string capabilityId, object? value)
    {
        var cap = device.FindCapability(capabilityId);
        if (cap is null || cap.Kind != CapabilityKind.Number || !TryToDouble(value, out var num)) return value;

        if (cap.Attributes.TryGetValue(CapabilityAttributeKeys.Min, out var mn) && TryToDouble(mn, out var min)) num = Math.Max(min, num);
        if (cap.Attributes.TryGetValue(CapabilityAttributeKeys.Max, out var mx) && TryToDouble(mx, out var max)) num = Math.Min(max, num);
        return num;
    }

    private static bool TryToDouble(object? v, out double result)
    {
        switch (v)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r): result = r; return true;
            default: result = 0; return false;
        }
    }
}
