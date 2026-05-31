using System.Globalization;
using System.Text.Json;

using Domovoy.Contracts.Capabilities;

namespace Domovoy.Connectivity.Adapters;

/// <summary>
/// Per-device translation table between Zigbee2MQTT's protocol vocabulary and Domovoy capabilities,
/// built once from the device's <c>definition.exposes</c>. Carries both directions so state can be
/// decoded and commands encoded without re-parsing exposes each time.
/// </summary>
internal sealed class Zigbee2MqttDeviceModel
{
    public required IReadOnlyList<Capability> Capabilities { get; init; }

    /// <summary>z2m property name (e.g. <c>brightness</c>) → how to decode it to a capability value.</summary>
    public required IReadOnlyDictionary<string, Z2mPropertyMap> ByZ2mProperty { get; init; }

    /// <summary>capability id (e.g. <c>brightness</c>) → how to encode it back to a z2m property/value.</summary>
    public required IReadOnlyDictionary<string, Z2mPropertyMap> ByCapabilityId { get; init; }
}

/// <summary>Bidirectional mapping for a single z2m property ↔ capability pair.</summary>
internal sealed record Z2mPropertyMap(
    string Z2mProperty,
    string CapabilityId,
    Func<JsonElement, object?> Decode,
    Func<object?, object?>? Encode);

/// <summary>
/// Translates Zigbee2MQTT messages to/from the Domovoy capability contract.
/// <list type="bullet">
/// <item><see cref="BuildModel"/>: <c>definition.exposes</c> → capabilities + decode/encode maps.</item>
/// <item><see cref="Decode"/>: a z2m state payload → normalized <see cref="Contracts.Devices.CapabilityState"/> values.</item>
/// <item><see cref="Encode"/>: a capability-addressed command → z2m payload.</item>
/// </list>
/// Scaling is applied here (brightness 0..254 ↔ 0..100, color_temp mireds ↔ Kelvin, ON/OFF ↔ bool),
/// so the rest of the system only ever sees normalized values.
/// </summary>
internal static class Zigbee2MqttCodec
{
    private sealed record Prop(
        string CapabilityId,
        CapabilityKind Kind,
        Func<JsonElement, object?> Decode,
        Func<object?, object?>? Encode);

    // Well-known z2m property → capability registry. Covers lighting, switches, common sensors
    // and climate. Extend here as new domains are added (color/cover/multi-gang are TODO).
    private static readonly Dictionary<string, Prop> Known = new(StringComparer.Ordinal)
    {
        ["state"]        = new(CapabilityIds.OnOff, CapabilityKind.Boolean, DecodeOnOff, EncodeOnOff),
        ["brightness"]   = new(CapabilityIds.Brightness, CapabilityKind.Number, DecodeBrightness, EncodeBrightness),
        ["color_temp"]   = new(CapabilityIds.ColorTemp, CapabilityKind.Number, DecodeMiredToKelvin, EncodeKelvinToMired),
        ["temperature"]  = new(CapabilityIds.Temperature, CapabilityKind.Number, DecodeNumber, null),
        ["humidity"]     = new(CapabilityIds.Humidity, CapabilityKind.Number, DecodeNumber, null),
        ["co2"]          = new(CapabilityIds.Co2, CapabilityKind.Number, DecodeNumber, null),
        ["occupancy"]    = new(CapabilityIds.Occupancy, CapabilityKind.Boolean, DecodeBool, null),
        ["contact"]      = new(CapabilityIds.Contact, CapabilityKind.Boolean, DecodeBool, null),
        ["battery"]      = new(CapabilityIds.Battery, CapabilityKind.Number, DecodeNumber, null),
        ["illuminance_lux"] = new(CapabilityIds.Illuminance, CapabilityKind.Number, DecodeNumber, null),
        ["illuminance"]  = new(CapabilityIds.Illuminance, CapabilityKind.Number, DecodeNumber, null),
        ["power"]        = new(CapabilityIds.Power, CapabilityKind.Number, DecodeNumber, null),
        ["energy"]       = new(CapabilityIds.Energy, CapabilityKind.Number, DecodeNumber, null),
        ["linkquality"]  = new(CapabilityIds.LinkQuality, CapabilityKind.Number, DecodeNumber, null),
    };

    // A lock's on/off lives under property "state" too, but means LOCK/UNLOCK — disambiguated by
    // the parent expose type during the exposes walk (see ResolveProp).
    private static readonly Prop LockProp =
        new(CapabilityIds.Lock, CapabilityKind.Boolean, DecodeLock, EncodeLock);

    // ====================================================================
    // Model building (definition.exposes -> capabilities + maps)
    // ====================================================================

    public static Zigbee2MqttDeviceModel BuildModel(JsonElement definition)
    {
        var capabilities = new List<Capability>();
        var byZ2m = new Dictionary<string, Z2mPropertyMap>(StringComparer.Ordinal);
        var byCap = new Dictionary<string, Z2mPropertyMap>(StringComparer.Ordinal);

        if (definition.ValueKind == JsonValueKind.Object &&
            definition.TryGetProperty("exposes", out var exposes) &&
            exposes.ValueKind == JsonValueKind.Array)
        {
            WalkExposes(exposes, parentType: null, (leaf, parentType) =>
            {
                if (!leaf.TryGetProperty("property", out var propEl) || propEl.ValueKind != JsonValueKind.String)
                    return;

                var z2mProperty = propEl.GetString();
                if (string.IsNullOrEmpty(z2mProperty)) return;

                var entry = ResolveProp(z2mProperty, parentType);
                if (entry is null || byCap.ContainsKey(entry.CapabilityId)) return;

                var writable = IsWritable(leaf) && entry.Encode is not null;
                capabilities.Add(BuildCapability(entry, leaf, writable));

                var map = new Z2mPropertyMap(z2mProperty, entry.CapabilityId, entry.Decode, writable ? entry.Encode : null);
                byZ2m[z2mProperty] = map;
                byCap[entry.CapabilityId] = map;
            });
        }

        return new Zigbee2MqttDeviceModel
        {
            Capabilities = capabilities,
            ByZ2mProperty = byZ2m,
            ByCapabilityId = byCap
        };
    }

    private static void WalkExposes(JsonElement array, string? parentType, Action<JsonElement, string?> onLeaf)
    {
        foreach (var item in array.EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;

            // Typed containers (light, switch, climate, lock, cover, fan, composite) nest leaves in "features".
            if (item.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                WalkExposes(features, type, onLeaf);
            else
                onLeaf(item, parentType);
        }
    }

    private static Prop? ResolveProp(string z2mProperty, string? parentType)
    {
        if (z2mProperty == "state" && string.Equals(parentType, "lock", StringComparison.OrdinalIgnoreCase))
            return LockProp;

        return Known.TryGetValue(z2mProperty, out var p) ? p : null;
    }

    private static Capability BuildCapability(Prop entry, JsonElement leaf, bool writable)
    {
        // Normalized capabilities use fixed (normalized) ranges, not the raw z2m range.
        switch (entry.CapabilityId)
        {
            case CapabilityIds.Brightness: return WellKnownCapabilities.Brightness(writable);
            case CapabilityIds.ColorTemp: return WellKnownCapabilities.ColorTemp(writable: writable);
        }

        // Everything else: build attributes from the expose definition (unit/min/max/step).
        var attrs = new Dictionary<string, object?> { [CapabilityAttributeKeys.Writable] = writable };

        if (leaf.TryGetProperty("unit", out var unit) && unit.ValueKind == JsonValueKind.String)
            attrs[CapabilityAttributeKeys.Unit] = unit.GetString();
        if (leaf.TryGetProperty("value_min", out var min) && min.ValueKind == JsonValueKind.Number)
            attrs[CapabilityAttributeKeys.Min] = min.GetDouble();
        if (leaf.TryGetProperty("value_max", out var max) && max.ValueKind == JsonValueKind.Number)
            attrs[CapabilityAttributeKeys.Max] = max.GetDouble();
        if (leaf.TryGetProperty("value_step", out var step) && step.ValueKind == JsonValueKind.Number)
            attrs[CapabilityAttributeKeys.Step] = step.GetDouble();

        return new Capability(entry.CapabilityId, entry.Kind, attrs);
    }

    private static bool IsWritable(JsonElement leaf)
    {
        // z2m "access" is a bitmask: bit 1 = published (read), bit 2 = set (write), bit 4 = get.
        if (leaf.TryGetProperty("access", out var access) && access.ValueKind == JsonValueKind.Number &&
            access.TryGetInt32(out var bits))
            return (bits & 0b010) != 0;
        return false;
    }

    // ====================================================================
    // Decode (z2m payload -> normalized capability values)
    // ====================================================================

    public static Contracts.Devices.CapabilityState Decode(Zigbee2MqttDeviceModel model, string payloadJson)
    {
        var state = new Contracts.Devices.CapabilityState();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(payloadJson); }
        catch (JsonException) { return state; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return state;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!model.ByZ2mProperty.TryGetValue(prop.Name, out var map)) continue;
                var value = map.Decode(prop.Value);
                if (value is not null) state.Set(map.CapabilityId, value);
            }
        }

        return state;
    }

    // ====================================================================
    // Encode (capability-addressed command -> z2m payload)
    // ====================================================================

    public static Dictionary<string, object> Encode(
        Zigbee2MqttDeviceModel model, IReadOnlyDictionary<string, object?> capabilitySet)
    {
        var payload = new Dictionary<string, object>();
        foreach (var (capabilityId, value) in capabilitySet)
        {
            if (!model.ByCapabilityId.TryGetValue(capabilityId, out var map) || map.Encode is null) continue;
            var encoded = map.Encode(value);
            if (encoded is not null) payload[map.Z2mProperty] = encoded;
        }
        return payload;
    }

    // ====================================================================
    // Value transforms
    // ====================================================================

    private static object? DecodeOnOff(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => string.Equals(v.GetString(), "ON", StringComparison.OrdinalIgnoreCase),
        _ => null
    };

    private static object? EncodeOnOff(object? v) => ToBool(v) ? "ON" : "OFF";

    private static object? DecodeBrightness(JsonElement v) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var raw)
            ? (object)(int)Math.Round(Clamp(raw, 0, 254) / 254.0 * 100.0)
            : null;

    private static object? EncodeBrightness(object? v) =>
        (int)Math.Round(Clamp(ToDouble(v), 0, 100) / 100.0 * 254.0);

    private static object? DecodeMiredToKelvin(JsonElement v) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var mired) && mired > 0
            ? (object)(int)Math.Round(1_000_000.0 / mired)
            : null;

    private static object? EncodeKelvinToMired(object? v)
    {
        var kelvin = ToDouble(v);
        return kelvin > 0 ? (int)Math.Round(1_000_000.0 / kelvin) : null;
    }

    private static object? DecodeNumber(JsonElement v) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (object)d : null;

    private static object? DecodeBool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => v.TryGetDouble(out var d) && d != 0,
        JsonValueKind.String => ParseBoolish(v.GetString()),
        _ => null
    };

    private static object? DecodeLock(JsonElement v) =>
        v.ValueKind == JsonValueKind.String
            ? string.Equals(v.GetString(), "LOCK", StringComparison.OrdinalIgnoreCase)
            : null;

    private static object? EncodeLock(object? v) => ToBool(v) ? "LOCK" : "UNLOCK";

    private static object? ParseBoolish(string? s) => s?.ToUpperInvariant() switch
    {
        "ON" or "TRUE" or "OPEN" or "DETECTED" or "YES" => true,
        "OFF" or "FALSE" or "CLOSED" or "CLEAR" or "NO" => false,
        _ => null
    };

    private static bool ToBool(object? v) => v switch
    {
        bool b => b,
        string s => s.Equals("ON", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("LOCK", StringComparison.OrdinalIgnoreCase),
        double d => d != 0,
        int i => i != 0,
        _ => false
    };

    private static double ToDouble(object? v) => v switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) => r,
        _ => 0
    };

    private static double Clamp(double value, double lo, double hi) => Math.Max(lo, Math.Min(hi, value));
}
