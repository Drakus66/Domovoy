// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

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

    public static Capability Energy() =>
        Number(CapabilityIds.Energy, "kWh", 0, null, writable: false);

    // --- Energy domain (roadmap Epic 3C) — read-only, platform/tariff-reported ---

    /// <summary>Current price per kWh (read-only). No min — dynamic tariffs allow negative (plunge) prices;
    /// <paramref name="unit"/> is the deployment's currency-per-kWh label (e.g. "₽/kWh").</summary>
    public static Capability Price(string? unit = null) =>
        Number(CapabilityIds.Price, unit, writable: false);

    /// <summary>Current tariff zone as an enum over the configured zone names (peak/day/night…), read-only.</summary>
    public static Capability TariffZone(IReadOnlyList<string> zones) =>
        Enum(CapabilityIds.TariffZone, zones, writable: false);

    // --- System virtual sensors (roadmap Epic 2L) — all read-only, platform-reported ---

    public static Capability SunElevation() =>
        Number(CapabilityIds.SunElevation, "°", -90, 90, writable: false);

    public static Capability SunAzimuth() =>
        Number(CapabilityIds.SunAzimuth, "°", 0, 360, writable: false);

    public static Capability IsDark() =>
        Boolean(CapabilityIds.IsDark, writable: false);

    public static Capability IsDay() =>
        Boolean(CapabilityIds.IsDay, writable: false);

    public static Capability Sunrise() =>
        Text(CapabilityIds.Sunrise, writable: false);

    public static Capability Sunset() =>
        Text(CapabilityIds.Sunset, writable: false);

    public static Capability TimeOfDay() =>
        Number(CapabilityIds.TimeOfDay, "min", 0, 1439, writable: false);

    public static Capability Clock() =>
        Text(CapabilityIds.Clock, writable: false);

    /// <summary>Local day-of-week as an enum over English day names (Monday..Sunday).</summary>
    public static readonly IReadOnlyList<string> DayNames =
        new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    public static Capability DayOfWeek() =>
        Enum(CapabilityIds.DayOfWeek, DayNames, writable: false);

    public static Capability IsWeekend() =>
        Boolean(CapabilityIds.IsWeekend, writable: false);

    public static Capability IsHoliday() =>
        Boolean(CapabilityIds.IsHoliday, writable: false);

    public static Capability CalendarDate() =>
        Text(CapabilityIds.CalendarDate, writable: false);

    /// <summary>Home mode as a writable enum on the Home virtual device (Epic 1G as a device):
    /// commanding it is THE way blocks/rules change the mode. Open set — well-known values listed.</summary>
    public static Capability HomeMode() =>
        Enum(CapabilityIds.HomeMode, Home.WellKnownModes.All, writable: true);

    /// <summary>Current power-source signal as a writable enum on the Power virtual device (Epic 3C-LM):
    /// commanding it is how a rule/user tells LoadManager which budget tier applies. Open set — well-known
    /// values listed (grid/grid_peak/battery/solar/off).</summary>
    public static Capability PowerSourceCap() =>
        Enum(CapabilityIds.PowerSource, Home.WellKnownPowerSources.All, writable: true);

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

    /// <summary>Builds a text capability.</summary>
    public static Capability Text(string id, bool writable = false) =>
        new(id, CapabilityKind.Text,
            new Dictionary<string, object?> { [CapabilityAttributeKeys.Writable] = writable });

    /// <summary>
    /// A writable geographic location — a text value <c>"lat,lon"</c> (optionally <c>"lat,lon|Label"</c>) the
    /// dashboard edits with a map picker (<see cref="CapabilityEditors.Geo"/>) rather than a raw text box.
    /// </summary>
    public static Capability Location(string id, bool writable = true) =>
        new(id, CapabilityKind.Text,
            new Dictionary<string, object?>
            {
                [CapabilityAttributeKeys.Writable] = writable,
                [CapabilityAttributeKeys.Editor] = CapabilityEditors.Geo,
            });

    /// <summary>A writable wall-clock time (<c>"HH:mm"</c>, empty allowed) edited with a time input.</summary>
    public static Capability TimeInput(string id, bool writable = true) =>
        new(id, CapabilityKind.Text,
            new Dictionary<string, object?>
            {
                [CapabilityAttributeKeys.Writable] = writable,
                [CapabilityAttributeKeys.Editor] = CapabilityEditors.Time,
            });

    /// <summary>Builds an enum capability over a fixed set of string values.</summary>
    public static Capability Enum(string id, IReadOnlyList<string> values, bool writable = true) =>
        new(id, CapabilityKind.Enum,
            new Dictionary<string, object?>
            {
                [CapabilityAttributeKeys.Values] = values,
                [CapabilityAttributeKeys.Writable] = writable
            });
}
