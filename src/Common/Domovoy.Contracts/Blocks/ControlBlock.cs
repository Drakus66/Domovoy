// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Blocks;

/// <summary>
/// A control-block instance (roadmap Epic 1H) — the stateful "middle layer" between stateless rules
/// (1A) and services. A block is a small periodic/feedback component (filter, thermostat loop, sequencer)
/// configured as data, like an <see cref="Domovoy.Contracts.Automations.AutomationRule"/>. Persisted in
/// the <c>control_blocks</c> collection, loaded by the AutomationService runtime.
///
/// Each block <b>projects as a virtual capability device</b> (<see cref="DeviceId"/>), so its outputs
/// (demand/setpoint/filtered signals) show up in <c>/devices</c>, accrue history/telemetry (P0-5) and
/// take commands for free. <b>Composition</b> is just one block's input bound to another (virtual)
/// device's capability — the graph emerges from who reads whose capability.
/// </summary>
public class ControlBlock
{
    /// <summary>Stable block id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Which built-in block type drives this instance (see the catalog, e.g. <c>ewma_filter</c>, <c>thermostat</c>).</summary>
    public string TypeId { get; set; } = string.Empty;

    /// <summary>
    /// Deterministic virtual capability-device id for this block (server-set on create via
    /// <see cref="Domovoy.Contracts.Devices.DeviceIdFactory"/>). The runtime announces a device with this
    /// id and publishes the block's outputs as its state.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Zone the virtual device belongs to (room/area); empty if unassigned.</summary>
    public string? ZoneId { get; set; }

    /// <summary>Disabled blocks are not ticked and do not emit.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Numeric parameters keyed by the block type's parameter name (e.g. <c>tau</c>, <c>setpoint</c>).</summary>
    public Dictionary<string, double> Params { get; set; } = new();

    /// <summary>Input port name → the device+capability it reads from (composition / wiring).</summary>
    public Dictionary<string, PortBinding> Inputs { get; set; } = new();

    /// <summary>
    /// Output capability id → the real device+capability it actuates (roadmap Epic 1D). When a bound
    /// output changes, the runtime publishes a <see cref="Domovoy.Contracts.Messaging.DeviceCommandV1"/>
    /// to the target (e.g. a thermostat block's <c>on_off</c> demand drives a boiler relay). Unbound
    /// outputs are still published as the block's own virtual-device state for visibility/composition.
    /// </summary>
    public Dictionary<string, PortBinding> Outputs { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Binds a block input port to a source device's capability (the wire of the block graph).</summary>
public class PortBinding
{
    public string DeviceId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
}
