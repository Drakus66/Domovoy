// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Heuristic device-archetype classifier (roadmap Epic 2D, v1). Maps a device's capability set + adapter/
/// model metadata onto a semantic <see cref="DeviceArchetypes"/> archetype, so the type-less capability
/// model still yields "this is a light / thermostat / motion sensor / lock". Pure and deterministic —
/// unit-tested without infrastructure. An ML.NET classifier (empirical, from behaviour) is a later upgrade
/// (Phase 2/3); a native adapter that declares its type explicitly (via <paramref name="model"/>) wins.
/// </summary>
public static class DeviceClassifier
{
    public static string Classify(IEnumerable<Capability> capabilities, string? adapterSource = null, string? model = null)
    {
        // 1) Explicit declaration wins: a native adapter may set Model to a known archetype token.
        if (DeviceArchetypes.IsKnown(model))
            return model!.ToLowerInvariant();

        // 2) Virtual control-block devices (1H) announce themselves with this adapter source.
        if (string.Equals(adapterSource, "ControlBlock", StringComparison.OrdinalIgnoreCase))
            return DeviceArchetypes.ControlBlock;

        // 2b) System virtual sensors (2L: sun/time/calendar) carry AdapterSource "System" and a
        // "system/<kind>" model. The kind after the slash is the archetype when it's a known token.
        if (string.Equals(adapterSource, "System", StringComparison.OrdinalIgnoreCase))
        {
            var kind = model is not null && model.StartsWith("system/", StringComparison.OrdinalIgnoreCase)
                ? model["system/".Length..]
                : null;
            return DeviceArchetypes.IsKnown(kind) ? kind!.ToLowerInvariant() : DeviceArchetypes.Sensor;
        }

        var caps = capabilities as IReadOnlyList<Capability> ?? capabilities.ToList();
        var ids = caps.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Has(string id) => ids.Contains(id);
        bool Writable(string id) => caps.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase) && IsWritable(c));

        // 3) Capability-set heuristics, most specific first.
        if (Has(CapabilityIds.Lock)) return DeviceArchetypes.Lock;
        if (Has(CapabilityIds.Valve)) return DeviceArchetypes.Valve;
        if (Has(CapabilityIds.Brightness) || Has(CapabilityIds.Color) || Has(CapabilityIds.ColorTemp))
            return DeviceArchetypes.Light;
        if (Has(CapabilityIds.TemperatureSetpoint)) return DeviceArchetypes.Thermostat;
        if (Has(CapabilityIds.Occupancy)) return DeviceArchetypes.Motion;
        if (Has(CapabilityIds.Contact)) return DeviceArchetypes.Contact;
        if (Writable(CapabilityIds.OnOff)) return DeviceArchetypes.Switch; // relay/plug with no light traits
        if (Has(CapabilityIds.Co2) || Has(CapabilityIds.Temperature) || Has(CapabilityIds.Humidity) || Has(CapabilityIds.Illuminance))
            return DeviceArchetypes.ClimateSensor;
        if (Has(CapabilityIds.Power) || Has(CapabilityIds.Energy)) return DeviceArchetypes.EnergyMeter;

        // 4) Anything that only reports is a generic sensor; otherwise unknown.
        return caps.Any(c => !IsWritable(c)) ? DeviceArchetypes.Sensor : DeviceArchetypes.Unknown;
    }

    /// <summary>
    /// Writability robust to JSON transport: over the bus, <see cref="Capability.Attributes"/> values arrive
    /// as <see cref="JsonElement"/> (not bool), so <see cref="Capability.IsWritable"/> alone would misread
    /// them. Mirrors EventInterceptor's attribute parsing.
    /// </summary>
    private static bool IsWritable(Capability c) =>
        c.Attributes.TryGetValue(CapabilityAttributeKeys.Writable, out var v) && v switch
        {
            bool b => b,
            JsonElement e => e.ValueKind == JsonValueKind.True,
            _ => false,
        };
}
