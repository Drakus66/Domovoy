namespace Domovoy.Contracts.Capabilities;

/// <summary>
/// Factory helpers for building the common capabilities with sensible default attributes.
/// Adapters use these when translating a protocol feature list (e.g. Zigbee2MQTT
/// <c>definition.exposes</c>) into <see cref="Capability"/> descriptors.
/// </summary>
public static class WellKnownCapabilities
{
    public static Capability OnOff(bool writable = true) =>
        Boolean(CapabilityIds.OnOff, writable);

    public static Capability Occupancy() =>
        Boolean(CapabilityIds.Occupancy, writable: false);

    public static Capability Contact() =>
        Boolean(CapabilityIds.Contact, writable: false);

    public static Capability Lock(bool writable = true) =>
        Boolean(CapabilityIds.Lock, writable);

    public static Capability Brightness(bool writable = true) =>
        Number(CapabilityIds.Brightness, "%", 0, 100, step: 1, writable: writable);

    public static Capability ColorTemp(int minK = 2200, int maxK = 6500, bool writable = true) =>
        Number(CapabilityIds.ColorTemp, "K", minK, maxK, step: 1, writable: writable);

    public static Capability Temperature() =>
        Number(CapabilityIds.Temperature, "°C", writable: false);

    public static Capability TemperatureSetpoint(double min = 5, double max = 35, double step = 0.5) =>
        Number(CapabilityIds.TemperatureSetpoint, "°C", min, max, step, writable: true);

    public static Capability Humidity() =>
        Number(CapabilityIds.Humidity, "%", 0, 100, writable: false);

    public static Capability Co2() =>
        Number(CapabilityIds.Co2, "ppm", 0, null, writable: false);

    public static Capability Valve() =>
        Number(CapabilityIds.Valve, "%", 0, 100, step: 1, writable: true);

    public static Capability Battery() =>
        Number(CapabilityIds.Battery, "%", 0, 100, writable: false);

    public static Capability Illuminance() =>
        Number(CapabilityIds.Illuminance, "lux", 0, null, writable: false);

    public static Capability Power() =>
        Number(CapabilityIds.Power, "W", 0, null, writable: false);

    public static Capability Color(bool writable = true) =>
        new(CapabilityIds.Color, CapabilityKind.Color,
            new Dictionary<string, object?> { [CapabilityAttributeKeys.Writable] = writable });

    /// <summary>Builds a boolean capability.</summary>
    public static Capability Boolean(string id, bool writable) =>
        new(id, CapabilityKind.Boolean,
            new Dictionary<string, object?> { [CapabilityAttributeKeys.Writable] = writable });

    /// <summary>Builds a numeric capability with optional range/unit.</summary>
    public static Capability Number(
        string id, string? unit = null, double? min = null, double? max = null,
        double? step = null, bool writable = false)
    {
        var attrs = new Dictionary<string, object?> { [CapabilityAttributeKeys.Writable] = writable };
        if (unit is not null) attrs[CapabilityAttributeKeys.Unit] = unit;
        if (min is not null) attrs[CapabilityAttributeKeys.Min] = min;
        if (max is not null) attrs[CapabilityAttributeKeys.Max] = max;
        if (step is not null) attrs[CapabilityAttributeKeys.Step] = step;
        return new Capability(id, CapabilityKind.Number, attrs);
    }

    /// <summary>Builds an enum capability over a fixed set of string values.</summary>
    public static Capability Enum(string id, IReadOnlyList<string> values, bool writable = true) =>
        new(id, CapabilityKind.Enum,
            new Dictionary<string, object?>
            {
                [CapabilityAttributeKeys.Values] = values,
                [CapabilityAttributeKeys.Writable] = writable
            });
}
