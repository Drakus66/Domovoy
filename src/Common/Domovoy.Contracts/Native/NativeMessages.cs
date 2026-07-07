// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Native;

using Domovoy.Contracts.Capabilities;

/// <summary>
/// Discovery announcement published by a native device to <see cref="NativeProtocol.AnnounceTopic"/>.
/// The device declares its capabilities directly (capability-native), so the adapter maps it onto a
/// <c>DeviceDescriptor</c> without any value translation.
/// </summary>
public sealed record NativeAnnounceV1
{
    /// <summary>Stable hardware id (MAC / chip id). Combined with the adapter source to derive the logical device id.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Human-friendly device name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional model string.</summary>
    public string? Model { get; init; }

    /// <summary>Optional firmware version.</summary>
    public string? Firmware { get; init; }

    /// <summary>
    /// Id of the board ("hub") fronting this device — the board's MQTT connection id. Lets the adapter
    /// map device→hub so it can mark every device of a hub offline when the board's Last-Will fires
    /// (<see cref="NativeProtocol.HubStatusTopic"/>). Null for single-device boards / legacy firmware.
    /// </summary>
    public string? Hub { get; init; }

    /// <summary>Capabilities the device exposes (on_off, brightness, temperature, …).</summary>
    public IReadOnlyList<Capability> Capabilities { get; init; } = [];
}
