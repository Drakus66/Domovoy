// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Globalization;
using System.Text;
using System.Text.Json;

using Domovoy.Contracts.Capabilities;

namespace Domovoy.Connectivity.Adapters;

/// <summary>
/// One decode/encode channel of an ESPHome entity — a single capability with its own MQTT state and/or
/// command topic. ESPHome's default (non-JSON) MQTT schema publishes each attribute on its own plain-payload
/// topic (a light's on/off and its brightness are separate topics), so a channel is the natural unit: decode
/// maps an inbound payload to a normalized value, encode maps a command value back to the wire payload.
/// </summary>
/// <param name="CapabilityId">Normalized capability id this channel carries (well-known or a custom esphome id).</param>
/// <param name="Capability">The capability descriptor (kind + range/unit/writable) advertised on discovery.</param>
/// <param name="StateTopic">Topic the device publishes this attribute on (null = command-only).</param>
/// <param name="CommandTopic">Topic commands are written to (null = read-only).</param>
/// <param name="Decode">Raw MQTT payload → normalized capability value (null to drop the sample).</param>
/// <param name="Encode">Normalized command value → raw MQTT payload (null = read-only).</param>
public sealed record EspChannel(
    string CapabilityId,
    Capability Capability,
    string? StateTopic,
    string? CommandTopic,
    Func<string, object?> Decode,
    Func<object?, string?>? Encode);

/// <summary>
/// One ESPHome entity parsed from an HA-MQTT-Discovery config message. Several entities of the same board
/// share one <see cref="DeviceKey"/> (from the config's <c>device.identifiers</c>), so the adapter groups them
/// into a single Domovoy device with the union of their channels — mirroring how a Native hub fronts many
/// devices as one.
/// </summary>
public sealed record EspHomeEntity(
    string DeviceKey,
    string DeviceName,
    string? Manufacturer,
    string? Model,
    string? AvailabilityTopic,
    string OnlinePayload,
    string OfflinePayload,
    IReadOnlyList<EspChannel> Channels);

/// <summary>
/// Translates Home-Assistant-style MQTT Discovery (which ESPHome speaks natively via its <c>mqtt:</c>
/// component) to/from the Domovoy capability contract (roadmap Epic 2J). This is the ESPHome analogue of
/// <see cref="Zigbee2MqttCodec"/>: a component (<c>sensor</c>/<c>binary_sensor</c>/<c>switch</c>/<c>light</c>/
/// <c>number</c>/<c>select</c>/<c>lock</c>) plus its <c>device_class</c>/<c>unit</c> map onto a well-known
/// capability id, and scaling/payload conventions (ON/OFF, brightness 0..scale ↔ 0..100) are applied here so
/// the rest of the system only ever sees normalized values.
///
/// <para>Config keys are read with their HA abbreviations as a fallback (<c>stat_t</c> for <c>state_topic</c>,
/// …) so the codec is robust to both ESPHome's default long keys and abbreviated publishers.</para>
/// </summary>
public static class EspHomeCodec
{
    /// <summary>
    /// Parse one discovery config into an entity (device grouping + channels), or null if the payload is
    /// empty (a retained-config clear), malformed, or the component is not supported in v1.
    /// </summary>
    /// <param name="component">The discovery component from the topic (sensor/switch/light/number/…).</param>
    /// <param name="objectId">The object id from the topic — used as the custom capability id for entities
    /// that don't map onto a well-known id (an unknown sensor/number/select).</param>
    /// <param name="configJson">The retained config payload.</param>
    public static EspHomeEntity? TryBuildEntity(string component, string objectId, string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(configJson); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var cfg = doc.RootElement;
            if (cfg.ValueKind != JsonValueKind.Object) return null;

            var (deviceKey, deviceName, manufacturer, model) = ReadDevice(cfg, objectId);
            var entityName = Str(cfg, "name", "name") ?? objectId;
            var (availTopic, onlinePayload, offlinePayload) = ReadAvailability(cfg);

            var channels = BuildChannels(component.ToLowerInvariant(), objectId, entityName, cfg);
            if (channels.Count == 0) return null;

            return new EspHomeEntity(
                deviceKey, deviceName, manufacturer, model, availTopic, onlinePayload, offlinePayload, channels);
        }
    }

    // ── component → channels ─────────────────────────────────────────────────────────────────────

    private static List<EspChannel> BuildChannels(string component, string objectId, string entityName, JsonElement cfg)
    {
        var stateTopic = Str(cfg, "state_topic", "stat_t");
        var commandTopic = Str(cfg, "command_topic", "cmd_t");
        var deviceClass = Str(cfg, "device_class", "dev_cla");
        var unit = Str(cfg, "unit_of_measurement", "unit_of_meas");

        switch (component)
        {
            case "sensor":
            {
                var capId = MapSensorCapability(deviceClass, objectId);
                var cap = WellKnownCapabilities.Number(capId, unit, writable: false);
                return One(new EspChannel(capId, cap, stateTopic, null, DecodeDouble, null));
            }

            case "binary_sensor":
            {
                var capId = MapBinaryCapability(deviceClass, objectId);
                var on = Str(cfg, "payload_on", "pl_on") ?? "ON";
                var off = Str(cfg, "payload_off", "pl_off") ?? "OFF";
                var cap = WellKnownCapabilities.Boolean(capId, writable: false);
                return One(new EspChannel(capId, cap, stateTopic, null, p => DecodeBool(p, on, off), null));
            }

            case "switch":
            {
                var on = Str(cfg, "payload_on", "pl_on") ?? "ON";
                var off = Str(cfg, "payload_off", "pl_off") ?? "OFF";
                var cap = WellKnownCapabilities.OnOff(writable: commandTopic is not null);
                return One(new EspChannel(
                    CapabilityIds.OnOff, cap, stateTopic, commandTopic,
                    p => DecodeBool(p, on, off), commandTopic is null ? null : v => ToBool(v) ? on : off));
            }

            case "light":
                // JSON schema carries all attributes as one JSON payload on a single topic; the default
                // schema splits them across per-attribute topics.
                return string.Equals(Str(cfg, "schema", "schema"), "json", StringComparison.OrdinalIgnoreCase)
                    ? BuildJsonLightChannels(cfg, stateTopic, commandTopic)
                    : BuildLightChannels(cfg, stateTopic, commandTopic);

            case "cover":
                return BuildCoverChannels(cfg, stateTopic, commandTopic);

            case "climate":
                return BuildClimateChannels(cfg, unit);

            case "fan":
                return BuildFanChannels(cfg, stateTopic, commandTopic);

            case "number":
            {
                var capId = string.Equals(deviceClass, "temperature", StringComparison.OrdinalIgnoreCase)
                    ? CapabilityIds.TemperatureSetpoint
                    : Sanitize(objectId);
                var min = Num(cfg, "min", "min");
                var max = Num(cfg, "max", "max");
                var step = Num(cfg, "step", "step");
                var cap = WellKnownCapabilities.Number(capId, unit, min, max, step, writable: commandTopic is not null);
                return One(new EspChannel(
                    capId, cap, stateTopic, commandTopic, DecodeDouble,
                    commandTopic is null ? null : EncodeNumber));
            }

            case "select":
            {
                var options = StrArray(cfg, "options", "options");
                if (options.Count == 0) return new();
                var capId = Sanitize(objectId);
                var cap = WellKnownCapabilities.Enum(capId, options, writable: commandTopic is not null);
                return One(new EspChannel(
                    capId, cap, stateTopic, commandTopic,
                    p => string.IsNullOrEmpty(p) ? null : p,
                    commandTopic is null ? null : v => v?.ToString()));
            }

            case "lock":
            {
                var payloadLock = Str(cfg, "payload_lock", "pl_lock") ?? "LOCK";
                var payloadUnlock = Str(cfg, "payload_unlock", "pl_unlk") ?? "UNLOCK";
                var cap = WellKnownCapabilities.Lock(writable: commandTopic is not null);
                return One(new EspChannel(
                    CapabilityIds.Lock, cap, stateTopic, commandTopic,
                    DecodeLock, commandTopic is null ? null : v => ToBool(v) ? payloadLock : payloadUnlock));
            }

            default:
                // Unknown/unsupported component (e.g. camera, text, event) → no channels.
                return new();
        }
    }

    // ESPHome default-schema light: on/off on command_topic, and (optionally) brightness on its own topic
    // scaled 0..brightness_scale (default 255) which we normalize to 0..100 like the Zigbee codec.
    private static List<EspChannel> BuildLightChannels(JsonElement cfg, string? stateTopic, string? commandTopic)
    {
        var channels = new List<EspChannel>();
        var on = Str(cfg, "payload_on", "pl_on") ?? "ON";
        var off = Str(cfg, "payload_off", "pl_off") ?? "OFF";

        var onOff = WellKnownCapabilities.OnOff(writable: commandTopic is not null);
        channels.Add(new EspChannel(
            CapabilityIds.OnOff, onOff, stateTopic, commandTopic,
            p => DecodeBool(p, on, off), commandTopic is null ? null : v => ToBool(v) ? on : off));

        var briState = Str(cfg, "brightness_state_topic", "bri_stat_t");
        var briCommand = Str(cfg, "brightness_command_topic", "bri_cmd_t");
        if (briState is not null || briCommand is not null)
        {
            var scale = Num(cfg, "brightness_scale", "bri_scl") ?? 255;
            var bri = WellKnownCapabilities.Brightness(writable: briCommand is not null);
            channels.Add(new EspChannel(
                CapabilityIds.Brightness, bri, briState, briCommand,
                p => DecodeScaled(p, scale),
                briCommand is null ? null : v => EncodeScaled(v, scale)));
        }

        return channels;
    }

    // JSON-schema light: one JSON payload ({state, brightness, …}) on the state/command topic. Each channel
    // decodes its own field from the shared payload and encodes a partial JSON command (the device merges it).
    private static List<EspChannel> BuildJsonLightChannels(JsonElement cfg, string? stateTopic, string? commandTopic)
    {
        var channels = new List<EspChannel>();

        var onOff = WellKnownCapabilities.OnOff(writable: commandTopic is not null);
        channels.Add(new EspChannel(
            CapabilityIds.OnOff, onOff, stateTopic, commandTopic,
            DecodeJsonState,
            commandTopic is null ? null : v => ToBool(v) ? "{\"state\":\"ON\"}" : "{\"state\":\"OFF\"}"));

        if (Flag(cfg, "brightness", "bri"))
        {
            var scale = Num(cfg, "brightness_scale", "bri_scl") ?? 255;
            var bri = WellKnownCapabilities.Brightness(writable: commandTopic is not null);
            channels.Add(new EspChannel(
                CapabilityIds.Brightness, bri, stateTopic, commandTopic,
                p => DecodeJsonBrightness(p, scale),
                commandTopic is null ? null : v => EncodeJsonBrightness(v, scale)));
        }

        return channels;
    }

    // Cover (blind/garage/gate): open/close as on_off (on = open), plus an optional 0..100 position channel.
    // Stop and tilt are out of v1 scope.
    private static List<EspChannel> BuildCoverChannels(JsonElement cfg, string? stateTopic, string? commandTopic)
    {
        var channels = new List<EspChannel>();

        var stateOpen = Str(cfg, "state_open", "stat_open") ?? "open";
        var stateClosed = Str(cfg, "state_closed", "stat_clsd") ?? "closed";
        var payloadOpen = Str(cfg, "payload_open", "pl_open") ?? "OPEN";
        var payloadClose = Str(cfg, "payload_close", "pl_cls") ?? "CLOSE";

        var onOff = WellKnownCapabilities.OnOff(writable: commandTopic is not null);
        channels.Add(new EspChannel(
            CapabilityIds.OnOff, onOff, stateTopic, commandTopic,
            p => DecodeCoverState(p, stateOpen, stateClosed),
            commandTopic is null ? null : v => ToBool(v) ? payloadOpen : payloadClose));

        var posState = Str(cfg, "position_topic", "pos_t");
        var posCommand = Str(cfg, "set_position_topic", "set_pos_t");
        if (posState is not null || posCommand is not null)
        {
            var pos = WellKnownCapabilities.Number(CapabilityIds.Position, "%", 0, 100, 1, writable: posCommand is not null);
            channels.Add(new EspChannel(
                CapabilityIds.Position, pos, posState, posCommand, DecodeDouble,
                posCommand is null ? null : EncodeNumber));
        }

        return channels;
    }

    // Climate (thermostat): a writable temperature_setpoint, a read current-temperature, and an hvac mode enum.
    private static List<EspChannel> BuildClimateChannels(JsonElement cfg, string? unit)
    {
        var channels = new List<EspChannel>();

        var tempCommand = Str(cfg, "temperature_command_topic", "temp_cmd_t");
        var tempState = Str(cfg, "temperature_state_topic", "temp_stat_t");
        if (tempCommand is not null || tempState is not null)
        {
            var min = Num(cfg, "min_temp", "min_temp");
            var max = Num(cfg, "max_temp", "max_temp");
            var step = Num(cfg, "temp_step", "temp_step");
            var sp = WellKnownCapabilities.Number(
                CapabilityIds.TemperatureSetpoint, unit ?? "°C", min, max, step, writable: tempCommand is not null);
            channels.Add(new EspChannel(
                CapabilityIds.TemperatureSetpoint, sp, tempState, tempCommand, DecodeDouble,
                tempCommand is null ? null : EncodeNumber));
        }

        var currentTemp = Str(cfg, "current_temperature_topic", "curr_temp_t");
        if (currentTemp is not null)
            channels.Add(new EspChannel(
                CapabilityIds.Temperature, WellKnownCapabilities.Temperature(), currentTemp, null, DecodeDouble, null));

        var modeCommand = Str(cfg, "mode_command_topic", "mode_cmd_t");
        var modeState = Str(cfg, "mode_state_topic", "mode_stat_t");
        if (modeCommand is not null || modeState is not null)
        {
            var modes = StrArray(cfg, "modes", "modes");
            if (modes.Count == 0) modes = new[] { "off", "heat", "cool", "auto" };
            var cap = WellKnownCapabilities.Enum(CapabilityIds.HvacMode, modes, writable: modeCommand is not null);
            channels.Add(new EspChannel(
                CapabilityIds.HvacMode, cap, modeState, modeCommand,
                p => string.IsNullOrEmpty(p) ? null : p,
                modeCommand is null ? null : v => v?.ToString()));
        }

        return channels;
    }

    // Fan: on/off plus an optional 0..100 speed (percentage) channel. Presets/oscillation are out of v1 scope.
    private static List<EspChannel> BuildFanChannels(JsonElement cfg, string? stateTopic, string? commandTopic)
    {
        var channels = new List<EspChannel>();

        var on = Str(cfg, "payload_on", "pl_on") ?? "ON";
        var off = Str(cfg, "payload_off", "pl_off") ?? "OFF";
        var onOff = WellKnownCapabilities.OnOff(writable: commandTopic is not null);
        channels.Add(new EspChannel(
            CapabilityIds.OnOff, onOff, stateTopic, commandTopic,
            p => DecodeBool(p, on, off), commandTopic is null ? null : v => ToBool(v) ? on : off));

        var pctState = Str(cfg, "percentage_state_topic", "pct_stat_t");
        var pctCommand = Str(cfg, "percentage_command_topic", "pct_cmd_t");
        if (pctState is not null || pctCommand is not null)
        {
            var speed = WellKnownCapabilities.Number(CapabilityIds.FanSpeed, "%", 0, 100, 1, writable: pctCommand is not null);
            channels.Add(new EspChannel(
                CapabilityIds.FanSpeed, speed, pctState, pctCommand, DecodeDouble,
                pctCommand is null ? null : EncodeNumber));
        }

        return channels;
    }

    // ── capability id mapping ────────────────────────────────────────────────────────────────────

    private static string MapSensorCapability(string? deviceClass, string objectId) =>
        (deviceClass?.ToLowerInvariant()) switch
        {
            "temperature" => CapabilityIds.Temperature,
            "humidity" => CapabilityIds.Humidity,
            "carbon_dioxide" => CapabilityIds.Co2,
            "illuminance" => CapabilityIds.Illuminance,
            "power" => CapabilityIds.Power,
            "energy" => CapabilityIds.Energy,
            "battery" => CapabilityIds.Battery,
            _ => Sanitize(objectId),
        };

    private static string MapBinaryCapability(string? deviceClass, string objectId) =>
        (deviceClass?.ToLowerInvariant()) switch
        {
            "motion" or "occupancy" or "presence" => CapabilityIds.Occupancy,
            "door" or "window" or "opening" or "garage_door" => CapabilityIds.Contact,
            _ => Sanitize(objectId),
        };

    // ── device + availability ────────────────────────────────────────────────────────────────────

    private static (string Key, string Name, string? Manufacturer, string? Model) ReadDevice(
        JsonElement cfg, string objectId)
    {
        string? key = null, name = null, manufacturer = null, model = null;

        if (TryProp(cfg, out var dev, "device", "dev") && dev.ValueKind == JsonValueKind.Object)
        {
            key = FirstIdentifier(dev);
            name = Str(dev, "name", "name");
            manufacturer = Str(dev, "manufacturer", "mf");
            model = Str(dev, "model", "mdl");
        }

        // Fallbacks so a board with a minimal device block still groups deterministically.
        key ??= Str(cfg, "unique_id", "uniq_id") ?? objectId;
        name ??= key;
        return (key, name, manufacturer, model);
    }

    private static string? FirstIdentifier(JsonElement dev)
    {
        if (TryProp(dev, out var ids, "identifiers", "ids"))
        {
            if (ids.ValueKind == JsonValueKind.String) return ids.GetString();
            if (ids.ValueKind == JsonValueKind.Array)
                foreach (var i in ids.EnumerateArray())
                    if (i.ValueKind == JsonValueKind.String) return i.GetString();
        }

        // connections: [["mac","AA:BB:.."]] — use the first value as a stable hardware id.
        if (TryProp(dev, out var conns, "connections", "cns") && conns.ValueKind == JsonValueKind.Array)
            foreach (var pair in conns.EnumerateArray())
                if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() >= 2)
                    return pair[1].GetString();

        return null;
    }

    private static (string? Topic, string Online, string Offline) ReadAvailability(JsonElement cfg)
    {
        var topic = Str(cfg, "availability_topic", "avty_t");
        var online = Str(cfg, "payload_available", "pl_avail") ?? "online";
        var offline = Str(cfg, "payload_not_available", "pl_not_avail") ?? "offline";

        if (topic is null && TryProp(cfg, out var avail, "availability", "avty") && avail.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in avail.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                topic = Str(a, "topic", "t");
                online = Str(a, "payload_available", "pl_avail") ?? online;
                offline = Str(a, "payload_not_available", "pl_not_avail") ?? offline;
                if (topic is not null) break;
            }
        }

        return (topic, online, offline);
    }

    // ── value transforms ─────────────────────────────────────────────────────────────────────────

    private static object? DecodeDouble(string payload) =>
        // ESPHome publishes "nan" for an unavailable sensor (and double.TryParse accepts "nan"/"inf") —
        // drop those instead of emitting a bogus reading.
        double.TryParse(payload, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
            ? d : null;

    private static object? DecodeBool(string payload, string on, string off)
    {
        var p = payload.Trim();
        if (string.Equals(p, on, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(p, off, StringComparison.OrdinalIgnoreCase)) return false;
        return ParseBoolish(p);
    }

    private static object? DecodeLock(string payload)
    {
        var p = payload.Trim();
        return p.ToUpperInvariant() switch
        {
            "LOCK" or "LOCKED" or "TRUE" or "ON" => true,
            "UNLOCK" or "UNLOCKED" or "FALSE" or "OFF" => false,
            _ => null,
        };
    }

    // Cover state → on_off (on = open). Opening/closing transients follow their destination.
    private static object? DecodeCoverState(string payload, string open, string closed)
    {
        var p = payload.Trim();
        if (string.Equals(p, open, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(p, closed, StringComparison.OrdinalIgnoreCase)) return false;
        return p.ToUpperInvariant() switch
        {
            "OPEN" or "OPENING" => true,
            "CLOSED" or "CLOSING" => false,
            _ => ParseBoolish(p),
        };
    }

    // JSON-schema light: pull the "state" field (ON/OFF) out of the shared JSON payload.
    private static object? DecodeJsonState(string payload)
    {
        try
        {
            using var d = JsonDocument.Parse(payload);
            if (d.RootElement.ValueKind == JsonValueKind.Object &&
                d.RootElement.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String)
                return DecodeBool(s.GetString() ?? "", "ON", "OFF");
        }
        catch (JsonException) { }
        return null;
    }

    // JSON-schema light: pull "brightness" (0..scale) out of the shared JSON payload, normalized to 0..100.
    private static object? DecodeJsonBrightness(string payload, double scale)
    {
        try
        {
            using var d = JsonDocument.Parse(payload);
            if (d.RootElement.ValueKind == JsonValueKind.Object &&
                d.RootElement.TryGetProperty("brightness", out var b) &&
                b.ValueKind == JsonValueKind.Number && b.TryGetDouble(out var raw) && scale > 0)
                return (int)Math.Round(Clamp(raw, 0, scale) / scale * 100.0);
        }
        catch (JsonException) { }
        return null;
    }

    // JSON-schema light brightness command: a partial JSON object (state ON implied by a non-zero brightness).
    private static string EncodeJsonBrightness(object? v, double scale)
    {
        var scaled = (int)Math.Round(Clamp(ToDouble(v), 0, 100) / 100.0 * scale);
        return $"{{\"state\":\"ON\",\"brightness\":{scaled.ToString(CultureInfo.InvariantCulture)}}}";
    }

    private static object? DecodeScaled(string payload, double scale) =>
        double.TryParse(payload, NumberStyles.Any, CultureInfo.InvariantCulture, out var raw) && double.IsFinite(raw) && scale > 0
            ? (object)(int)Math.Round(Clamp(raw, 0, scale) / scale * 100.0)
            : null;

    private static string EncodeScaled(object? v, double scale) =>
        ((int)Math.Round(Clamp(ToDouble(v), 0, 100) / 100.0 * scale)).ToString(CultureInfo.InvariantCulture);

    private static string EncodeNumber(object? v) => ToDouble(v).ToString(CultureInfo.InvariantCulture);

    private static object? ParseBoolish(string? s) => s?.ToUpperInvariant() switch
    {
        "ON" or "TRUE" or "OPEN" or "DETECTED" or "YES" or "1" => true,
        "OFF" or "FALSE" or "CLOSED" or "CLEAR" or "NO" or "0" => false,
        _ => null,
    };

    private static bool ToBool(object? v) => v switch
    {
        bool b => b,
        string s => s.Equals("ON", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("LOCK", StringComparison.OrdinalIgnoreCase),
        double d => d != 0,
        int i => i != 0,
        _ => false,
    };

    private static double ToDouble(object? v) => v switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) => r,
        _ => 0,
    };

    private static double Clamp(double value, double lo, double hi) => Math.Max(lo, Math.Min(hi, value));

    // ── json helpers (long key, then HA abbreviation) ────────────────────────────────────────────

    private static bool TryProp(JsonElement obj, out JsonElement value, string key, string abbrev)
    {
        if (obj.TryGetProperty(key, out value)) return true;
        if (!string.Equals(key, abbrev, StringComparison.Ordinal) && obj.TryGetProperty(abbrev, out value)) return true;
        value = default;
        return false;
    }

    private static string? Str(JsonElement obj, string key, string abbrev) =>
        TryProp(obj, out var v, key, abbrev) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement obj, string key, string abbrev) =>
        TryProp(obj, out var v, key, abbrev) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    // A boolean-ish config flag: JSON true, or a "true"/"1" string (publishers vary).
    private static bool Flag(JsonElement obj, string key, string abbrev)
    {
        if (!TryProp(obj, out var v, key, abbrev)) return false;
        return v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b)
            || (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d != 0);
    }

    private static IReadOnlyList<string> StrArray(JsonElement obj, string key, string abbrev)
    {
        if (!TryProp(obj, out var v, key, abbrev) || v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s) list.Add(s);
        return list;
    }

    private static List<EspChannel> One(EspChannel channel) => new() { channel };

    // Custom capability id from an object id: lowercase, non-alphanumerics collapsed to underscores.
    private static string Sanitize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        var s = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(s) ? "value" : s;
    }
}
