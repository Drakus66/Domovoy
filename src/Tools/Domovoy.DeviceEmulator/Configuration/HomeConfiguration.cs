// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;
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

    /// <summary>
    /// Shared outdoor temperature that couples into every <see cref="ThermalZones"/> heat-loss term. Exposed as a
    /// normal sensor device (so the server sees it) but its value is driven by the emulator's physics, not random.
    /// </summary>
    [JsonPropertyName("external_temperature")]
    public ExternalTemperatureConfiguration? ExternalTemperature { get; set; }

    /// <summary>
    /// Physics couplings between a heater (convector) and a temperature sensor that live <b>only</b> inside the
    /// emulator — the server never learns the link, which is exactly the dependency an explicit thermostat (with a
    /// declared source sensor) or the ML model has to be told about / discover.
    /// </summary>
    [JsonPropertyName("thermal_zones")]
    public List<ThermalZoneConfiguration> ThermalZones { get; set; } = new();
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

    /// <summary>
    /// Optional initial value. Without it Booleans start OFF and Numbers at the mid-point of their range; set it so a
    /// convector can boot on at full power (<c>on_off = true</c>, <c>power = 100</c>) and heat straight away.
    /// </summary>
    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }
}

/// <summary>Drives the shared outdoor temperature: a slow day/night sine around a user-movable centre.</summary>
public class ExternalTemperatureConfiguration
{
    /// <summary>Sensor device whose reading mirrors the outdoor temperature (must exist in <c>devices</c>).</summary>
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = "outdoor";

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = "temperature";

    /// <summary>Starting centre temperature (°C). Moving the sensor's slider in the UI re-centres the drift here.</summary>
    [JsonPropertyName("initial")]
    public double Initial { get; set; } = 5;

    /// <summary>Amplitude (°C) of the slow sine wobble around the centre; 0 = perfectly held (fully manual).</summary>
    [JsonPropertyName("drift_amplitude")]
    public double DriftAmplitude { get; set; } = 2;

    /// <summary>Period (minutes) of one full drift cycle — compressed day/night, kept short for testing.</summary>
    [JsonPropertyName("drift_period_minutes")]
    public double DriftPeriodMinutes { get; set; } = 20;
}

/// <summary>
/// One heater→sensor physics coupling. Each tick the room temperature integrates
/// <c>T += heat_gain_at_full·p − ambient_coupling·(T − T_ext) + noise</c>, where <c>p</c> is the heater's effective
/// power (0–1). Steady state is <c>T_ext + (heat_gain_at_full / ambient_coupling)·p</c>, so a colder outdoors needs
/// more power to hold the same room temperature — the lag comes free from the discrete integration.
/// </summary>
public class ThermalZoneConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("heater_device")]
    public string HeaterDevice { get; set; } = string.Empty;

    /// <summary>Numeric power capability (0–100 %) on the heater device.</summary>
    [JsonPropertyName("heater_capability")]
    public string HeaterCapability { get; set; } = "power";

    [JsonPropertyName("sensor_device")]
    public string SensorDevice { get; set; } = string.Empty;

    [JsonPropertyName("sensor_capability")]
    public string SensorCapability { get; set; } = "temperature";

    /// <summary>°C added per tick at 100 % power (heater size).</summary>
    [JsonPropertyName("heat_gain_at_full")]
    public double HeatGainAtFull { get; set; } = 3.0;

    /// <summary>Fraction of the room↔outdoor gap lost per tick (insulation; also sets the response time constant).</summary>
    [JsonPropertyName("ambient_coupling")]
    public double AmbientCoupling { get; set; } = 0.12;

    /// <summary>Starting room temperature (°C).</summary>
    [JsonPropertyName("initial_temp")]
    public double InitialTemp { get; set; } = 18;

    /// <summary>±°C of measurement noise added each tick, so the signal isn't perfectly clean for the ML.</summary>
    [JsonPropertyName("noise")]
    public double Noise { get; set; } = 0.05;
}
