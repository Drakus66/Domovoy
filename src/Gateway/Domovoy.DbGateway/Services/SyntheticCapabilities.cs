// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.DbGateway.Models;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Capabilities the platform maintains on a device on top of what its adapter reports (roadmap Epic 3C-D).
/// Today that is the <c>power</c>/<c>energy</c> pair a device gains when the user turns on energy accounting:
/// the AutomationService estimates/integrates the series, and carrying it as a real capability is what lets
/// the rest of the system — accounting, trends, rules, dashboards, discovery — treat an estimated device
/// exactly like a metered one, with no special path anywhere.
/// <para>Re-applied on every announce (the discovery upsert rewrites the whole capability list) and whenever
/// the profile changes, so it is written as a pure function of (adapter capabilities, profile).</para>
/// </summary>
public static class SyntheticCapabilities
{
    /// <summary>The adapter's own capabilities, plus the synthetic ones the profile calls for. Idempotent:
    /// previously-added synthetic entries are dropped first, so this never accumulates duplicates.</summary>
    public static List<CapabilityDocument> Apply(
        IEnumerable<CapabilityDocument> capabilities, EnergyProfile? profile)
    {
        var result = capabilities.Where(c => !c.Synthetic).ToList();
        if (profile?.Track != true) return result;

        var metered = result.Any(c => c.Id == CapabilityIds.Energy);
        var hasPowerMeter = result.Any(c => c.Id == CapabilityIds.Power);

        // Power is only synthesized when it has to be estimated: a device with its own wattmeter reports it,
        // and a device with an energy counter needs no estimate at all.
        if (!metered && !hasPowerMeter)
            result.Add(Synthetic(CapabilityIds.Power, "W"));
        // Energy is synthesized whenever the device has no counter of its own — integrated from the measured
        // or estimated power.
        if (!metered)
            result.Add(Synthetic(CapabilityIds.Energy, "kWh"));

        return result;
    }

    private static CapabilityDocument Synthetic(string id, string unit) => new()
    {
        Id = id,
        Kind = nameof(CapabilityKind.Number),
        Writable = false,
        Unit = unit,
        Min = 0,
        Synthetic = true,
    };
}
