// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Connectivity.Adapters;
using Domovoy.Contracts.Capabilities;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Pure unit tests for the ESPHome HA-MQTT-Discovery codec (roadmap Epic 2J). No infrastructure — verifies
/// discovery-config → capabilities, state decode and command encode for each supported component, plus device
/// grouping and availability, mirroring the Zigbee2MQTT-codec test approach.
/// </summary>
public sealed class EspHomeCodecTests
{
    private static EspHomeEntity Build(string component, string objectId, string json)
    {
        var entity = EspHomeCodec.TryBuildEntity(component, objectId, json);
        Assert.NotNull(entity);
        return entity!;
    }

    private static EspChannel Cap(EspHomeEntity e, string capId) =>
        Assert.Single(e.Channels, c => c.CapabilityId == capId);

    [Fact]
    public void Sensor_Temperature_MapsToTemperature_AndDecodes()
    {
        var e = Build("sensor", "living_temp", """
            {"name":"Living Temp","state_topic":"domovoy/esphome/esp1/living_temp/state",
             "unit_of_measurement":"°C","device_class":"temperature","unique_id":"esp1_living_temp",
             "device":{"identifiers":["esp1"],"name":"ESP1","manufacturer":"Espressif","model":"ESP32"}}
            """);

        Assert.Equal("esp1", e.DeviceKey);
        Assert.Equal("ESP1", e.DeviceName);
        var ch = Cap(e, CapabilityIds.Temperature);
        Assert.Equal(CapabilityKind.Number, ch.Capability.Kind);
        Assert.False(ch.Capability.IsWritable);
        Assert.Equal(23.5, Assert.IsType<double>(ch.Decode("23.5")));
        Assert.Null(ch.Decode("nan"));
    }

    [Fact]
    public void BinarySensor_Motion_MapsToOccupancy_DefaultPayloads()
    {
        var e = Build("binary_sensor", "hall_motion", """
            {"name":"Hall Motion","state_topic":"domovoy/esphome/esp1/hall_motion/state",
             "device_class":"motion","device":{"identifiers":["esp1"]}}
            """);

        var ch = Cap(e, CapabilityIds.Occupancy);
        Assert.Equal(CapabilityKind.Boolean, ch.Capability.Kind);
        Assert.Equal(true, ch.Decode("ON"));
        Assert.Equal(false, ch.Decode("OFF"));
    }

    [Fact]
    public void Switch_RoundTrips_OnOff()
    {
        var e = Build("switch", "relay", """
            {"name":"Relay","state_topic":"domovoy/esphome/esp1/relay/state",
             "command_topic":"domovoy/esphome/esp1/relay/command","device":{"identifiers":["esp1"]}}
            """);

        var ch = Cap(e, CapabilityIds.OnOff);
        Assert.True(ch.Capability.IsWritable);
        Assert.Equal("domovoy/esphome/esp1/relay/command", ch.CommandTopic);
        Assert.Equal(true, ch.Decode("ON"));
        Assert.NotNull(ch.Encode);
        Assert.Equal("ON", ch.Encode!(true));
        Assert.Equal("OFF", ch.Encode!(false));
    }

    [Fact]
    public void Number_TemperatureDeviceClass_MapsToSetpoint_AndEncodes()
    {
        var e = Build("number", "target_temp", """
            {"name":"Target","state_topic":"s","command_topic":"c","device_class":"temperature",
             "min":5,"max":30,"step":0.5,"unit_of_measurement":"°C","device":{"identifiers":["esp1"]}}
            """);

        var ch = Cap(e, CapabilityIds.TemperatureSetpoint);
        Assert.True(ch.Capability.IsWritable);
        Assert.Equal(21.5, Assert.IsType<double>(ch.Decode("21.5")));
        Assert.Equal("21.5", ch.Encode!(21.5));
    }

    [Fact]
    public void Number_Unknown_UsesSanitizedObjectId()
    {
        var e = Build("number", "Fan Speed", """
            {"name":"Fan","state_topic":"s","command_topic":"c","min":0,"max":100,"device":{"identifiers":["esp1"]}}
            """);
        Assert.Single(e.Channels, c => c.CapabilityId == "fan_speed");
    }

    [Fact]
    public void Select_ExposesEnum_AndEncodes()
    {
        var e = Build("select", "mode", """
            {"name":"Mode","state_topic":"s","command_topic":"c","options":["off","eco","comfort"],
             "device":{"identifiers":["esp1"]}}
            """);

        var ch = Cap(e, "mode");
        Assert.Equal(CapabilityKind.Enum, ch.Capability.Kind);
        var values = Assert.IsAssignableFrom<IReadOnlyList<string>>(ch.Capability.Attributes[CapabilityAttributeKeys.Values]);
        Assert.Equal(new[] { "off", "eco", "comfort" }, values);
        Assert.Equal("comfort", ch.Decode("comfort"));
        Assert.Equal("eco", ch.Encode!("eco"));
    }

    [Fact]
    public void Light_WithBrightness_ExposesTwoChannels_AndScales()
    {
        var e = Build("light", "lamp", """
            {"name":"Lamp","state_topic":"s","command_topic":"c",
             "brightness_state_topic":"bs","brightness_command_topic":"bc",
             "device":{"identifiers":["esp1"]}}
            """);

        var onOff = Cap(e, CapabilityIds.OnOff);
        Assert.Equal("ON", onOff.Encode!(true));

        var bri = Cap(e, CapabilityIds.Brightness);
        Assert.Equal(50, Assert.IsType<int>(bri.Decode("128")));  // 128/255 ≈ 50%
        Assert.Equal("255", bri.Encode!(100));                    // 100% → full scale (255)
    }

    [Fact]
    public void Lock_RoundTrips()
    {
        var e = Build("lock", "front", """
            {"name":"Front","state_topic":"s","command_topic":"c","device":{"identifiers":["esp1"]}}
            """);
        var ch = Cap(e, CapabilityIds.Lock);
        Assert.Equal(true, ch.Decode("LOCKED"));
        Assert.Equal("LOCK", ch.Encode!(true));
        Assert.Equal("UNLOCK", ch.Encode!(false));
    }

    [Fact]
    public void Availability_ParsedWithDefaults()
    {
        var withTopic = Build("switch", "relay", """
            {"name":"Relay","state_topic":"s","command_topic":"c","availability_topic":"domovoy/esphome/esp1/status",
             "device":{"identifiers":["esp1"]}}
            """);
        Assert.Equal("domovoy/esphome/esp1/status", withTopic.AvailabilityTopic);
        Assert.Equal("online", withTopic.OnlinePayload);
        Assert.Equal("offline", withTopic.OfflinePayload);
    }

    [Fact]
    public void AbbreviatedKeys_AreSupported()
    {
        // HA discovery abbreviations: stat_t / cmd_t / dev_cla / uniq_id / dev / ids.
        var e = Build("sensor", "co2", """
            {"name":"CO2","stat_t":"s","dev_cla":"carbon_dioxide","uniq_id":"esp1_co2","dev":{"ids":["esp1"]}}
            """);
        Assert.Equal("esp1", e.DeviceKey);
        Assert.Single(e.Channels, c => c.CapabilityId == CapabilityIds.Co2);
    }

    [Fact]
    public void SameBoard_Entities_ShareDeviceKey()
    {
        var a = Build("sensor", "t", """
            {"name":"T","state_topic":"s1","device_class":"temperature","device":{"identifiers":["esp1"]}}
            """);
        var b = Build("switch", "r", """
            {"name":"R","state_topic":"s2","command_topic":"c","device":{"identifiers":["esp1"]}}
            """);
        Assert.Equal(a.DeviceKey, b.DeviceKey);
    }

    [Fact]
    public void Cover_OpenClose_AndPosition()
    {
        var e = Build("cover", "garage", """
            {"name":"Garage","state_topic":"s","command_topic":"c",
             "position_topic":"ps","set_position_topic":"pc","device":{"identifiers":["esp1"]}}
            """);

        var onOff = Cap(e, CapabilityIds.OnOff);
        Assert.Equal(true, onOff.Decode("open"));
        Assert.Equal(false, onOff.Decode("closed"));
        Assert.Equal("OPEN", onOff.Encode!(true));
        Assert.Equal("CLOSE", onOff.Encode!(false));

        var pos = Cap(e, CapabilityIds.Position);
        Assert.True(pos.Capability.IsWritable);
        Assert.Equal(60d, Assert.IsType<double>(pos.Decode("60")));
        Assert.Equal("60", pos.Encode!(60));
    }

    [Fact]
    public void Climate_Setpoint_CurrentTemp_AndMode()
    {
        var e = Build("climate", "thermostat", """
            {"name":"Thermostat","temperature_command_topic":"tc","temperature_state_topic":"ts",
             "current_temperature_topic":"ct","mode_command_topic":"mc","mode_state_topic":"ms",
             "modes":["off","heat","cool"],"min_temp":10,"max_temp":30,"temp_step":0.5,
             "device":{"identifiers":["esp1"]}}
            """);

        var sp = Cap(e, CapabilityIds.TemperatureSetpoint);
        Assert.True(sp.Capability.IsWritable);
        Assert.Equal("21.5", sp.Encode!(21.5));

        var cur = Cap(e, CapabilityIds.Temperature);
        Assert.False(cur.Capability.IsWritable);
        Assert.Equal(19.4, Assert.IsType<double>(cur.Decode("19.4")));

        var mode = Cap(e, CapabilityIds.HvacMode);
        Assert.Equal(CapabilityKind.Enum, mode.Capability.Kind);
        Assert.Equal("heat", mode.Decode("heat"));
        Assert.Equal("cool", mode.Encode!("cool"));
    }

    [Fact]
    public void Fan_OnOff_AndSpeed()
    {
        var e = Build("fan", "vent", """
            {"name":"Vent","state_topic":"s","command_topic":"c",
             "percentage_state_topic":"ps","percentage_command_topic":"pc","device":{"identifiers":["esp1"]}}
            """);

        var onOff = Cap(e, CapabilityIds.OnOff);
        Assert.Equal("ON", onOff.Encode!(true));

        var speed = Cap(e, CapabilityIds.FanSpeed);
        Assert.True(speed.Capability.IsWritable);
        Assert.Equal(75d, Assert.IsType<double>(speed.Decode("75")));
        Assert.Equal("40", speed.Encode!(40));
    }

    [Fact]
    public void JsonLight_SharedTopic_DecodesStateAndBrightness()
    {
        var e = Build("light", "lamp", """
            {"name":"Lamp","schema":"json","state_topic":"st","command_topic":"cm","brightness":true,
             "device":{"identifiers":["esp1"]}}
            """);

        var onOff = Cap(e, CapabilityIds.OnOff);
        var bri = Cap(e, CapabilityIds.Brightness);
        // Both channels share the one JSON state topic.
        Assert.Equal("st", onOff.StateTopic);
        Assert.Equal("st", bri.StateTopic);

        Assert.Equal(true, onOff.Decode("""{"state":"ON","brightness":128}"""));
        Assert.Equal(50, Assert.IsType<int>(bri.Decode("""{"state":"ON","brightness":128}""")));  // 128/255 ≈ 50%

        Assert.Equal("""{"state":"ON"}""", onOff.Encode!(true));
        Assert.Equal("""{"state":"OFF"}""", onOff.Encode!(false));
        Assert.Equal("""{"state":"ON","brightness":255}""", bri.Encode!(100));
    }

    [Fact]
    public void UnsupportedComponent_ReturnsNull() =>
        Assert.Null(EspHomeCodec.TryBuildEntity("camera", "cam", """{"name":"Cam","device":{"identifiers":["esp1"]}}"""));

    [Fact]
    public void EmptyPayload_ReturnsNull() =>
        Assert.Null(EspHomeCodec.TryBuildEntity("sensor", "t", ""));
}
