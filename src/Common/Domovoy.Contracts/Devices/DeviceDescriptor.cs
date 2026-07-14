// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Devices;

using Domovoy.Contracts.Capabilities;

/// <summary>
/// Stable physical identity of a device, independent of its (renameable) friendly name.
/// Used to resolve incoming protocol messages back to a logical device and to route commands.
/// </summary>
/// <param name="AdapterSource">Adapter that owns the device, e.g. <c>"Zigbee2Mqtt"</c>.</param>
/// <param name="HardwareId">Hardware address (Zigbee IEEE / MAC) — stable across renames; null if unknown.</param>
/// <param name="StateTopic">Protocol topic the device publishes state on (if topic-based).</param>
/// <param name="CommandTopic">Protocol topic commands are sent to (if topic-based).</param>
/// <param name="AdditionalTopics">Any extra named topics (availability, etc.).</param>
public sealed record DeviceIdentity(
    string AdapterSource,
    string? HardwareId = null,
    string? StateTopic = null,
    string? CommandTopic = null,
    IReadOnlyDictionary<string, string>? AdditionalTopics = null);

/// <summary>
/// Capability-based description of a logical device. Replaces the closed
/// <c>GlobalEntityTypes</c> enum + per-type entity classes: a device is whatever capabilities
/// it exposes, so climate zones, valves, locks and multi-capability devices need no new types.
/// Carried by discovery and stored as the canonical device record.
/// </summary>
/// <param name="Id">Logical device id (stable across protocol renames).</param>
/// <param name="Name">Human-friendly name.</param>
/// <param name="ZoneId">Area/zone the device belongs to (room, plot section); <see cref="Guid.Empty"/> if unassigned.</param>
/// <param name="Identity">Physical identity / routing info.</param>
/// <param name="Capabilities">What the device can do/report.</param>
/// <param name="Manufacturer">Vendor, if known.</param>
/// <param name="Model">Model, if known.</param>
public sealed record DeviceDescriptor(
    Guid Id,
    string Name,
    Guid ZoneId,
    DeviceIdentity Identity,
    IReadOnlyList<Capability> Capabilities,
    string? Manufacturer = null,
    string? Model = null)
{
    /// <summary>True if the device exposes the given capability id.</summary>
    public bool HasCapability(string capabilityId) =>
        Capabilities.Any(c => c.Id == capabilityId);
}
