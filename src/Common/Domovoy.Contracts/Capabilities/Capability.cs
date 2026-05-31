namespace Domovoy.Contracts.Capabilities;

/// <summary>
/// Value semantics of a capability. Lets generic consumers (UI rendering, automation
/// conditions, ML feature typing) reason about a capability without knowing its concrete id.
/// </summary>
public enum CapabilityKind
{
    /// <summary>Boolean state/command (on_off, occupancy, contact, lock).</summary>
    Boolean,

    /// <summary>Numeric measurement or setpoint (brightness, temperature, co2, power, battery).</summary>
    Number,

    /// <summary>One of a fixed set of string values (hvac mode, preset).</summary>
    Enum,

    /// <summary>Color value (hex or xy/hs object).</summary>
    Color,

    /// <summary>Arbitrary text.</summary>
    Text,

    /// <summary>Command-only capability with no readable state (identify, toggle).</summary>
    Action
}

/// <summary>
/// Stable, well-known capability identifiers. Adapters map protocol-native features onto these;
/// automations, UI and ML reference them. Plugins MAY introduce custom ids — namespace them with
/// a vendor prefix (e.g. <c>"acme:foo"</c>) to avoid clashing with the core vocabulary.
/// </summary>
public static class CapabilityIds
{
    public const string OnOff = "on_off";                          // bool
    public const string Brightness = "brightness";                 // number, 0..100 (%)
    public const string Color = "color";                           // color (hex)
    public const string ColorTemp = "color_temp";                  // number, Kelvin
    public const string Temperature = "temperature";               // number, °C (read)
    public const string TemperatureSetpoint = "temperature_setpoint"; // number, °C (write)
    public const string Humidity = "humidity";                     // number, %
    public const string Co2 = "co2";                               // number, ppm
    public const string Occupancy = "occupancy";                   // bool (motion/presence)
    public const string Contact = "contact";                       // bool (open/closed)
    public const string Lock = "lock";                             // bool (locked/unlocked)
    public const string Valve = "valve";                           // number, 0..100 (%) — irrigation/heating
    public const string Power = "power";                           // number, W
    public const string Energy = "energy";                         // number, kWh
    public const string Battery = "battery";                       // number, %
    public const string Illuminance = "illuminance";               // number, lux
    public const string LinkQuality = "link_quality";              // number, 0..255
}

/// <summary>
/// Keys for the <see cref="Capability.Attributes"/> bag. Describe value range/unit/access so a
/// generic consumer can render controls, clamp commands and build ML features.
/// </summary>
public static class CapabilityAttributeKeys
{
    public const string Unit = "unit";       // "%", "°C", "ppm", "lux", "W", "kWh"
    public const string Min = "min";
    public const string Max = "max";
    public const string Step = "step";
    public const string Writable = "writable"; // bool — can be commanded
    public const string Readable = "readable"; // bool — reports state
    public const string Values = "values";     // string[] — allowed values for Enum kind
}

/// <summary>
/// A single thing a device can do or report (e.g. on_off, brightness, temperature).
/// A device is described as a set of capabilities (see DeviceDescriptor) rather than a closed type.
/// </summary>
/// <param name="Id">Capability identifier — a well-known <see cref="CapabilityIds"/> value or a namespaced custom id.</param>
/// <param name="Kind">Value semantics of the capability.</param>
/// <param name="Attributes">Range/unit/access metadata — see <see cref="CapabilityAttributeKeys"/>.</param>
public sealed record Capability(
    string Id,
    CapabilityKind Kind,
    IReadOnlyDictionary<string, object?> Attributes)
{
    /// <summary>True when the capability can be written via a command.</summary>
    public bool IsWritable =>
        Attributes.TryGetValue(CapabilityAttributeKeys.Writable, out var w) && w is true;

    /// <summary>True when the capability reports readable state.</summary>
    public bool IsReadable =>
        !Attributes.TryGetValue(CapabilityAttributeKeys.Readable, out var r) || r is true;
}
