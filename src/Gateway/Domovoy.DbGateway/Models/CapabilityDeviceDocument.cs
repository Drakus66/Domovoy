// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Persisted read-model of a capability device (roadmap Step 4). Written by the EventInterceptor
/// from the capability contract (DeviceDiscoveredV1 / DeviceStateReportV1) and read by the WebUI.
/// Uses plain BSON-friendly types (no contract records/interfaces) so Mongo serialization is simple.
/// </summary>
public class CapabilityDeviceDocument
{
    /// <summary>Logical device id (GUID as string) — stable across protocol renames/restarts.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    /// <summary>Raw name reported by the adapter at discovery (e.g. a Zigbee IEEE address like
    /// <c>0xa4c1383e04dbda65</c>). Recomputed on every (re)announce — do not treat as user-editable.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>User-set friendly name (roadmap Epic 3G-alias); null ⇒ the UI falls back to a type-derived
    /// label or <see cref="Name"/>. Like <see cref="Archetype"/>/<see cref="ZoneId"/> the discovery path
    /// never writes it, so a re-announce can't clobber the user's choice. Set via the alias endpoint.</summary>
    public string? Alias { get; set; }

    /// <summary>Owning adapter, e.g. "Zigbee2Mqtt" or "DomovoyNative".</summary>
    public string AdapterSource { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string ZoneId { get; set; } = string.Empty;

    public List<CapabilityDocument> Capabilities { get; set; } = new();

    /// <summary>Latest normalized state, keyed by capability id (on_off, brightness, temperature, …).</summary>
    public Dictionary<string, object> State { get; set; } = new();

    public bool IsOnline { get; set; }

    /// <summary>Auto-inferred semantic archetype (roadmap Epic 2D) — recomputed by the classifier on each discovery.</summary>
    public string AutoArchetype { get; set; } = Domovoy.Contracts.Devices.DeviceArchetypes.Unknown;

    /// <summary>User-set archetype override; null ⇒ use <see cref="AutoArchetype"/>. The discovery path never clobbers it.</summary>
    public string? Archetype { get; set; }

    /// <summary>Legacy energy role (Epic 3C v1) — superseded by <see cref="EnergyProfile"/> and migrated away at
    /// startup by <c>EnergyProfileMigration</c>: <c>"mains"</c> ⇒ <see cref="Models.EnergyProfile.Role"/>,
    /// <c>"excluded"</c> ⇒ <c>Track=false</c>. Kept on the model only so the migration can read old documents.</summary>
    public string? EnergyRole { get; set; }

    /// <summary>User-configured energy accounting for this device (Epic 3C-D): whether it counts toward kWh
    /// totals and — for a device with no meter — how to estimate its draw. Null ⇒ defaults (a metered device
    /// counts, an unmetered one doesn't). Set only via the energy-profile endpoint — the discovery upsert never
    /// writes it (like <see cref="Archetype"/>/<see cref="LoadShedding"/>).</summary>
    public EnergyProfile? EnergyProfile { get; set; }

    /// <summary>User-configured load-shedding profile (Epic 3C-LM); null ⇒ the device is not managed by
    /// LoadManager (safe no-op default). Set only via the load-shedding endpoint — the discovery upsert
    /// never writes it (like <see cref="Archetype"/>/<see cref="EnergyRole"/>).</summary>
    public LoadSheddingProfile? LoadShedding { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-device energy accounting (roadmap Epic 3C-D). Replaces the block-based Powercalc/integrator setup: the
/// consumption of a device is configured on the device itself. A metered device (its adapter reports
/// <c>energy</c>) needs nothing here; a device that reports only <c>power</c> gets its watts integrated; a
/// device with no measurement at all is estimated from <see cref="MaxPowerW"/> scaled by its regulator.
/// </summary>
public class EnergyProfile
{
    /// <summary>Count this device toward kWh totals. Null ⇒ the default: a metered device counts, an unmetered
    /// one does not (turning the toggle on is what starts estimating it).</summary>
    public bool? Track { get; set; }

    /// <summary>Null/<c>"consumer"</c> ⇒ a normal load; <c>"mains"</c> ⇒ a whole-home/aggregate meter that is
    /// surfaced as the grand total but excluded from the per-device sum (no double counting).</summary>
    public string? Role { get; set; }

    /// <summary>Draw (W) at full load — the nameplate figure. Required to estimate a device with no meter;
    /// also what the load coordinator budgets with.</summary>
    public double? MaxPowerW { get; set; }

    /// <summary>Draw (W) at the regulator's minimum while still on (a dimmable floor).</summary>
    public double? MinPowerW { get; set; }

    /// <summary>Draw (W) while off — standby / vampire load.</summary>
    public double? StandbyPowerW { get; set; }

    /// <summary>Numeric capability (0..100) whose value scales the draw between <see cref="MinPowerW"/> and
    /// <see cref="MaxPowerW"/> — brightness for a light, fan speed / valve position elsewhere. Null ⇒ the
    /// device draws <see cref="MaxPowerW"/> whenever it is on.</summary>
    public string? ScaleCapabilityId { get; set; }

    /// <summary>Circuit (breaker) this device hangs on, from the power topology (Epic 3C-D stage 2); the phase
    /// is inherited from that circuit. Null ⇒ not mapped to the electrical tree yet.</summary>
    public string? CircuitId { get; set; }
}

/// <summary>
/// Per-device load-shedding configuration (roadmap Epic 3C-LM): whether/how LoadManager may curtail or
/// switch off this device when the household's power budget is exceeded.
/// </summary>
public class LoadSheddingProfile
{
    /// <summary>Opt-in — false ⇒ the device is never touched even if a mode tier says sheddable.</summary>
    public bool Enabled { get; set; }

    /// <summary>Never shed regardless of mode (safety devices) — overrides every mode tier.</summary>
    public bool Protected { get; set; }

    /// <summary>The numeric writable capability LoadManager commands to <b>curtail</b> (reduce) this
    /// device (e.g. "brightness", "fan_speed") — only used when <see cref="Curtailable"/>. Turning a load
    /// fully off always targets <c>on_off</c> directly, regardless of this value.</summary>
    public string ControlCapabilityId { get; set; } = "on_off";

    /// <summary>True ⇒ LoadManager tries a reduced (not fully off) command first, via <see cref="CurtailedValue"/>.</summary>
    public bool Curtailable { get; set; }

    /// <summary>Value commanded on <see cref="ControlCapabilityId"/> when curtailing (e.g. a dimmed brightness).</summary>
    public double? CurtailedValue { get; set; }

    /// <summary>Value commanded on <see cref="ControlCapabilityId"/> when restoring from curtailed.</summary>
    public double? RestoreValue { get; set; }

    /// <summary>Criticality tier per home mode: "critical" (never shed) | "sheddable" (see
    /// <see cref="ModePriority"/>) | "unmanaged" (excluded from load-shedding in that mode). Modes with no
    /// entry default to "unmanaged" — the safe no-op.</summary>
    public Dictionary<string, string> ModeTier { get; set; } = new();

    /// <summary>Shed priority per mode when the tier is "sheddable" — lower sheds first.</summary>
    public Dictionary<string, int> ModePriority { get; set; } = new();
}

/// <summary>Flattened capability descriptor for persistence/UI.</summary>
public class CapabilityDocument
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // Boolean | Number | Enum | Color | Text | Action
    public bool Writable { get; set; }
    public string? Unit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>Numeric step for a Number control (slider precision).</summary>
    public double? Step { get; set; }

    /// <summary>Allowed values for an Enum capability — drives a dropdown in the UI.</summary>
    public List<string>? Values { get; set; }

    /// <summary>UI editor hint for a writable value (e.g. "geo", "time") — see CapabilityEditors.</summary>
    public string? Editor { get; set; }

    /// <summary>True for a capability the platform adds on top of what the adapter reports — today the
    /// <c>power</c>/<c>energy</c> series a tracked device gets from its energy profile (Epic 3C-D). Re-applied
    /// on every (re)announce by the EventInterceptor, and labelled as an estimate in the UI.</summary>
    public bool Synthetic { get; set; }
}
