using System.Text.Json;

namespace Domovoy.Contracts.Devices;

using Domovoy.Contracts.Capabilities;

/// <summary>
/// Normalized device state keyed by capability id (e.g. <c>on_off</c> → true, <c>brightness</c> → 100).
/// This is the single representation produced by adapter codecs and consumed by handlers, the UI,
/// automations and ML — replacing the protocol-native, per-handler ad-hoc dictionaries.
/// <para>
/// Values are stored as boxed primitives or <see cref="JsonElement"/> (after deserialization);
/// the typed accessors tolerate both so callers never branch on the wire form.
/// </para>
/// </summary>
public sealed class CapabilityState
{
    private readonly Dictionary<string, object?> _values;

    public CapabilityState() => _values = new();

    public CapabilityState(IDictionary<string, object?> values) =>
        _values = new Dictionary<string, object?>(values);

    /// <summary>Raw capability-id → value map.</summary>
    public IReadOnlyDictionary<string, object?> Values => _values;

    public bool Has(string capabilityId) => _values.ContainsKey(capabilityId);

    public CapabilityState Set(string capabilityId, object? value)
    {
        _values[capabilityId] = value;
        return this;
    }

    // ---- Well-known typed accessors (ergonomic shortcuts) -------------------

    public bool? OnOff() => TryGetBool(CapabilityIds.OnOff, out var v) ? v : null;
    public int? Brightness() => TryGetInt(CapabilityIds.Brightness, out var v) ? v : null;
    public double? Temperature() => TryGetDouble(CapabilityIds.Temperature, out var v) ? v : null;
    public double? Humidity() => TryGetDouble(CapabilityIds.Humidity, out var v) ? v : null;
    public double? Co2() => TryGetDouble(CapabilityIds.Co2, out var v) ? v : null;
    public int? Battery() => TryGetInt(CapabilityIds.Battery, out var v) ? v : null;

    // ---- Generic typed getters (handle boxed primitives and JsonElement) ----

    public bool TryGetBool(string capabilityId, out bool value)
    {
        value = default;
        if (!_values.TryGetValue(capabilityId, out var raw) || raw is null) return false;
        switch (raw)
        {
            case bool b: value = b; return true;
            case string s when bool.TryParse(s, out var bs): value = bs; return true;
            case JsonElement { ValueKind: JsonValueKind.True }: value = true; return true;
            case JsonElement { ValueKind: JsonValueKind.False }: value = false; return true;
            default: return false;
        }
    }

    public bool TryGetInt(string capabilityId, out int value)
    {
        value = default;
        if (TryGetDouble(capabilityId, out var d)) { value = (int)Math.Round(d); return true; }
        return false;
    }

    public bool TryGetDouble(string capabilityId, out double value)
    {
        value = default;
        if (!_values.TryGetValue(capabilityId, out var raw) || raw is null) return false;
        switch (raw)
        {
            case double dd: value = dd; return true;
            case float f: value = f; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case string s when double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var ds):
                value = ds; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetDouble(out var jd):
                value = jd; return true;
            default: return false;
        }
    }

    public bool TryGetString(string capabilityId, out string value)
    {
        value = string.Empty;
        if (!_values.TryGetValue(capabilityId, out var raw) || raw is null) return false;
        switch (raw)
        {
            case string s: value = s; return true;
            case JsonElement { ValueKind: JsonValueKind.String } je: value = je.GetString() ?? ""; return true;
            default: value = raw.ToString() ?? ""; return true;
        }
    }
}
