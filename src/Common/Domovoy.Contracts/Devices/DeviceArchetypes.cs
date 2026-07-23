// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Devices;

/// <summary>
/// Semantic device archetype (roadmap Epic 2D) — the inferred <i>kind</i> of a device (light, thermostat,
/// motion sensor, lock, …) on top of the type-less capability model. Used for UI (icons/grouping/controls),
/// smarter automation suggestions and ML features. An <b>open</b> set of lowercase tokens (like capabilities
/// and modes), so deployments/adapters can introduce their own without a contract change. The classifier
/// derives it from a device's capability set + adapter/model metadata; a native adapter that declares a type
/// explicitly takes precedence; the user can override it.
/// </summary>
public static class DeviceArchetypes
{
    public const string Light = "light";
    public const string Switch = "switch";              // generic relay/plug
    public const string Thermostat = "thermostat";       // has a temperature setpoint
    public const string ClimateSensor = "climate_sensor"; // temp/humidity/co2/illuminance (read-only)
    public const string Motion = "motion";               // occupancy / presence
    public const string Contact = "contact";             // door/window
    public const string Lock = "lock";
    public const string Valve = "valve";                 // irrigation / heating valve
    public const string EnergyMeter = "energy_meter";    // power/energy metering
    public const string Sensor = "sensor";               // generic read-only sensor
    public const string ControlBlock = "control_block";  // a virtual device projected by a control block (1H)
    public const string Sun = "sun";                     // system sun sensor (elevation/azimuth/is_dark…) (2L)
    public const string Clock = "clock";                 // system time sensor (time_of_day/clock) (2L)
    public const string Calendar = "calendar";           // system calendar sensor (day_of_week/weekend/holiday) (2L)
    public const string Tariff = "tariff";               // system tariff entity (price/tariff_zone) (3C)
    public const string Unknown = "unknown";

    /// <summary>Canonical list for UI selection (open set — custom archetypes may also appear).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Light, Switch, Thermostat, ClimateSensor, Motion, Contact, Lock, Valve, EnergyMeter, Sensor,
        ControlBlock, Sun, Clock, Calendar, Tariff, Unknown,
    };

    public static bool IsKnown(string? archetype) =>
        archetype is not null && All.Contains(archetype, StringComparer.OrdinalIgnoreCase);
}
