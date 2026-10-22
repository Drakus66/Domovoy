// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Which capability regulates a device's draw (roadmap Epic 3C-D). The energy profile may name one
/// explicitly; otherwise the first well-known 0..100 % regulator the device actually has is used, so a dimmable
/// lamp or a variable convector is estimated proportionally without the user configuring anything.
/// </summary>
public static class EnergyScale
{
    /// <summary>Well-known percentage regulators, in the order they are auto-detected.</summary>
    public static readonly IReadOnlyList<string> Candidates = new[]
    {
        CapabilityIds.Brightness, CapabilityIds.FanSpeed, CapabilityIds.Position,
    };

    /// <summary>The regulator capability id for this device, or null when it is a plain on/off load.</summary>
    public static string? Resolve(
        string? configured, IEnumerable<DbGatewayClient.CapabilitySnapshot> capabilities)
    {
        var ids = capabilities.Select(c => c.Id).ToList();
        if (!string.IsNullOrWhiteSpace(configured))
            return ids.Contains(configured) ? configured : null;
        return Candidates.FirstOrDefault(ids.Contains);
    }
}
