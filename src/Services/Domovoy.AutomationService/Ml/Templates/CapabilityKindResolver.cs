// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Resolves the <see cref="CapabilityKind"/> of a target capability so the trainer can pick applicable
/// templates (roadmap Epic 2I). v1 maps the well-known core vocabulary; unknown ids default to
/// <see cref="CapabilityKind.Number"/> (the common ML target). A later phase resolves the kind from the live
/// device descriptor instead of this static map, which also covers plugin/custom capability ids.
/// </summary>
public static class CapabilityKindResolver
{
    private static readonly HashSet<string> Booleans = new(StringComparer.OrdinalIgnoreCase)
    {
        CapabilityIds.OnOff, CapabilityIds.Occupancy, CapabilityIds.Contact, CapabilityIds.Lock,
    };

    public static CapabilityKind KindOf(string capabilityId) =>
        Booleans.Contains(capabilityId) ? CapabilityKind.Boolean : CapabilityKind.Number;
}
