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

    /// <summary>
    /// CO₂ ⇄ ventilation couplings, again hidden from the server. A CO₂ sensor climbs from the room's steady
    /// occupant load and is scrubbed by the shared supply-exhaust fan, so it holds steady when the fan sits at
    /// <see cref="Co2ZoneConfiguration.EquilibriumFraction"/> of full speed, rises when the fan slows/stops and
    /// falls faster the harder the fan runs.
    /// </summary>
    [JsonPropertyName("co2_zones")]
    public List<Co2ZoneConfiguration> Co2Zones { get; set; } = new();

    /// <summary>Water-tank pressure ⇄ pump-relay couplings (pressure bleeds off, the pump refills it up to a cap).</summary>
    [JsonPropertyName("pressure_zones")]
    public List<PressureZoneConfiguration> PressureZones { get; set; } = new();

    /// <summary>
    /// Per-device electrical metering. The emulator derives each appliance's live draw (W) from its own state
    /// (on/off × a level dial × rated watts) and integrates it into a cumulative <c>energy</c> (kWh) series, so
    /// the server's energy accounting (Epic 3C, which keys off the <c>energy</c> capability) sees real metered
    /// consumers with no per-device integrator block to configure. The <c>energy</c> (and optional live
    /// <c>power</c>) capabilities are added to the device automatically — they need not be declared in <c>devices</c>.
    /// </summary>
    [JsonPropertyName("power_meters")]
    public List<PowerMeterConfiguration> PowerMeters { get; set; } = new();
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

/// <summary>
/// One ventilation→CO₂ coupling. Each tick <c>CO₂ += generation − (generation / equilibrium_fraction)·f</c>,
/// where <c>f</c> is the fan's effective speed (0–1). At <c>f = equilibrium_fraction</c> the two terms cancel and
/// the reading holds; below it CO₂ climbs (fastest with the fan off), above it CO₂ falls (faster the harder the
/// fan runs). An optional presence gate models a room (the bathroom) that only produces CO₂ while occupied: when
/// unoccupied its reading decays back toward <see cref="Min"/> instead of being generated.
/// </summary>
public class Co2ZoneConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The shared supply-exhaust fan device whose speed scrubs this room's CO₂.</summary>
    [JsonPropertyName("vent_device")]
    public string VentDevice { get; set; } = string.Empty;

    /// <summary>Numeric fan-speed capability (0–100 %) on the ventilation device.</summary>
    [JsonPropertyName("vent_capability")]
    public string VentCapability { get; set; } = "fan_speed";

    /// <summary>Optional on/off capability on the fan; when present and <c>false</c> the fan scrubs nothing.</summary>
    [JsonPropertyName("vent_switch_capability")]
    public string? VentSwitchCapability { get; set; } = "on_off";

    [JsonPropertyName("sensor_device")]
    public string SensorDevice { get; set; } = string.Empty;

    [JsonPropertyName("sensor_capability")]
    public string SensorCapability { get; set; } = "co2";

    /// <summary>ppm produced in the room per tick (steady occupant load).</summary>
    [JsonPropertyName("generation")]
    public double Generation { get; set; } = 15;

    /// <summary>Fan fraction (0–1) at which generation and scrubbing balance, so the reading holds steady.</summary>
    [JsonPropertyName("equilibrium_fraction")]
    public double EquilibriumFraction { get; set; } = 0.5;

    /// <summary>Starting CO₂ reading (ppm).</summary>
    [JsonPropertyName("initial")]
    public double Initial { get; set; } = 700;

    [JsonPropertyName("min")]
    public double Min { get; set; } = 400;

    [JsonPropertyName("max")]
    public double Max { get; set; } = 2000;

    /// <summary>±ppm of measurement noise added each tick.</summary>
    [JsonPropertyName("noise")]
    public double Noise { get; set; } = 5;

    /// <summary>Optional presence device; while it reads unoccupied the room produces no CO₂ and decays to <see cref="Min"/>.</summary>
    [JsonPropertyName("presence_device")]
    public string? PresenceDevice { get; set; }

    [JsonPropertyName("presence_capability")]
    public string PresenceCapability { get; set; } = "occupancy";

    /// <summary>Fraction of the gap to <see cref="Min"/> shed per tick while the gated room is unoccupied.</summary>
    [JsonPropertyName("idle_decay")]
    public double IdleDecay { get; set; } = 0.3;
}

/// <summary>
/// One pump-relay→pressure coupling. Water-tank pressure bleeds off by <see cref="DrainRate"/> per tick, and while
/// the pump relay is on it instead climbs by <see cref="FillRate"/> per tick, clamped to <see cref="Max"/> atm.
/// </summary>
public class PressureZoneConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("pump_device")]
    public string PumpDevice { get; set; } = string.Empty;

    /// <summary>Boolean relay capability on the pump device.</summary>
    [JsonPropertyName("pump_capability")]
    public string PumpCapability { get; set; } = "on_off";

    [JsonPropertyName("sensor_device")]
    public string SensorDevice { get; set; } = string.Empty;

    [JsonPropertyName("sensor_capability")]
    public string SensorCapability { get; set; } = "pressure";

    /// <summary>atm added per tick while the pump runs.</summary>
    [JsonPropertyName("fill_rate")]
    public double FillRate { get; set; } = 0.6;

    /// <summary>atm bled off per tick while the pump is idle.</summary>
    [JsonPropertyName("drain_rate")]
    public double DrainRate { get; set; } = 0.12;

    [JsonPropertyName("initial")]
    public double Initial { get; set; } = 4;

    [JsonPropertyName("min")]
    public double Min { get; set; } = 1;

    /// <summary>Pressure cap (atm) — the relief-valve limit the pump can never push past.</summary>
    [JsonPropertyName("max")]
    public double Max { get; set; } = 10;

    /// <summary>±atm of measurement noise added each tick.</summary>
    [JsonPropertyName("noise")]
    public double Noise { get; set; } = 0.02;
}

/// <summary>
/// Turns one device into an electrical meter. Live draw is
/// <c>on ? max(standby, rated·level) : standby</c>, where <c>on</c> follows <see cref="SwitchCapability"/> and
/// <c>level</c> (0–1) follows the optional <see cref="LevelCapability"/> dial (a convector's power %, a lamp's
/// brightness, a fan's speed); a device with no level dial draws its full rating whenever it is on. The draw is
/// integrated into a cumulative <c>energy</c> (kWh) series and, when <see cref="ExposePower"/> is set, also
/// published as a live <c>power</c> (W) reading.
/// </summary>
public class PowerMeterConfiguration
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Draw (W) at full rating with the level dial at 100 %.</summary>
    [JsonPropertyName("rated_watts")]
    public double RatedWatts { get; set; }

    /// <summary>Boolean on/off gate; empty means the device is always drawing.</summary>
    [JsonPropertyName("switch_capability")]
    public string? SwitchCapability { get; set; } = "on_off";

    /// <summary>Optional 0–100 modulation dial (e.g. <c>power</c>, <c>brightness</c>, <c>fan_speed</c>); absent = full draw when on.</summary>
    [JsonPropertyName("level_capability")]
    public string? LevelCapability { get; set; }

    [JsonPropertyName("level_max")]
    public double LevelMax { get; set; } = 100;

    /// <summary>Standby draw (W) while off — vampire load; 0 for a clean off.</summary>
    [JsonPropertyName("standby_watts")]
    public double StandbyWatts { get; set; }

    /// <summary>Whether to also publish a live read-only <c>power</c> (W). Off for a convector, whose <c>power</c> id is its % dial.</summary>
    [JsonPropertyName("expose_power")]
    public bool ExposePower { get; set; } = true;

    [JsonPropertyName("power_capability")]
    public string PowerCapability { get; set; } = "power";

    [JsonPropertyName("energy_capability")]
    public string EnergyCapability { get; set; } = "energy";

    /// <summary>±W of measurement noise on the live <c>power</c> reading (never fed into the energy integral).</summary>
    [JsonPropertyName("noise_watts")]
    public double NoiseWatts { get; set; }
}
