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
    public const string OnOff = "on_off";                          // bool (generic/heating demand)
    public const string CoolDemand = "cool_demand";                // bool (cooling demand — thermostat cool/heat-cool)
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
    public const string Position = "position";                     // number, 0..100 (%) — cover/blind position
    public const string HvacMode = "hvac_mode";                    // enum — off/heat/cool/auto (climate)
    public const string FanSpeed = "fan_speed";                    // number, 0..100 (%) — fan speed

    // --- System virtual sensors (roadmap Epic 2L) — reported by the platform, no physical device ---
    public const string SunElevation = "sun_elevation";            // number, ° above horizon (negative = below)
    public const string SunAzimuth = "sun_azimuth";                // number, ° clockwise from north (0=N, 90=E)
    public const string IsDark = "is_dark";                        // bool — sun below the horizon at the site
    public const string IsDay = "is_day";                          // bool — sun above the horizon at the site
    public const string Sunrise = "sunrise";                       // text, local HH:mm of today's sunrise
    public const string Sunset = "sunset";                         // text, local HH:mm of today's sunset
    public const string TimeOfDay = "time_of_day";                 // number, local minutes since midnight (0..1439)
    public const string Clock = "clock";                           // text, local HH:mm
    public const string DayOfWeek = "day_of_week";                 // enum — Monday..Sunday (local)
    public const string IsWeekend = "is_weekend";                  // bool — today is a configured weekend day
    public const string IsHoliday = "is_holiday";                  // bool — today is a configured holiday
    public const string CalendarDate = "date";                     // text, local yyyy-MM-dd
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
    public const string Editor = "editor";     // string — UI editor hint for a writable value (see CapabilityEditors)
}

/// <summary>
/// Well-known values for <see cref="CapabilityAttributeKeys.Editor"/> — a hint to the UI on how to edit a
/// writable capability whose raw <see cref="CapabilityKind"/> isn't specific enough. The value stays a plain
/// string on the wire; the editor only changes the input control the dashboard renders.
/// </summary>
public static class CapabilityEditors
{
    /// <summary>A geographic location, value <c>"lat,lon"</c> (optionally <c>"lat,lon|Label"</c>) — map picker.</summary>
    public const string Geo = "geo";

    /// <summary>A wall-clock time of day, value <c>"HH:mm"</c> — time input (empty allowed).</summary>
    public const string Time = "time";
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
