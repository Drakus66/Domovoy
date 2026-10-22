// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;

namespace Domovoy.AutomationService.Ml.Templates;

/// <summary>
/// Resolves the <see cref="CapabilityKind"/> of a target capability so the trainer can pick applicable
/// templates (roadmap Epic 2I). <b>Descriptor-based</b>: the kind comes from the live capability-device
/// read-model — the value type the adapter itself declared — refreshed by <see cref="RefreshLoop"/> alongside
/// the devices. That covers plugin/custom capability ids, and it is what makes the enum branch reachable at
/// all: the static v1 map returned only Boolean or Number, so an <see cref="CapabilityKind.Enum"/> target
/// (<c>hvac_mode</c>, <c>power_source</c>, …) was silently trained as a regression and
/// <see cref="ScheduleMulticlassTemplate"/> — the whole Phase-3 multiclass cell, and with it the
/// <c>ml_selector</c> governor — could never be selected.
///
/// <para>Falls back to the well-known core vocabulary when a capability isn't in the read-model (a target
/// whose device is offline/not yet discovered, a cold start, or the gateway being unreachable), and to
/// <see cref="CapabilityKind.Number"/> for genuinely unknown ids — the common ML target.</para>
/// </summary>
public sealed class CapabilityKindResolver
{
    // Well-known core vocabulary, used when the live read-model has nothing to say about an id.
    private static readonly Dictionary<string, CapabilityKind> WellKnown = new(StringComparer.OrdinalIgnoreCase)
    {
        [CapabilityIds.OnOff] = CapabilityKind.Boolean,
        [CapabilityIds.CoolDemand] = CapabilityKind.Boolean,
        [CapabilityIds.Occupancy] = CapabilityKind.Boolean,
        [CapabilityIds.Contact] = CapabilityKind.Boolean,
        [CapabilityIds.Lock] = CapabilityKind.Boolean,
        [CapabilityIds.Presence] = CapabilityKind.Boolean,
        [CapabilityIds.AnyoneHome] = CapabilityKind.Boolean,
        [CapabilityIds.IsDark] = CapabilityKind.Boolean,
        [CapabilityIds.IsDay] = CapabilityKind.Boolean,
        [CapabilityIds.IsWeekend] = CapabilityKind.Boolean,
        [CapabilityIds.IsHoliday] = CapabilityKind.Boolean,

        [CapabilityIds.HvacMode] = CapabilityKind.Enum,
        [CapabilityIds.TariffZone] = CapabilityKind.Enum,
        [CapabilityIds.PowerSource] = CapabilityKind.Enum,
        [CapabilityIds.HomeMode] = CapabilityKind.Enum,
        [CapabilityIds.DayOfWeek] = CapabilityKind.Enum,

        [CapabilityIds.Color] = CapabilityKind.Color,

        [CapabilityIds.Sunrise] = CapabilityKind.Text,
        [CapabilityIds.Sunset] = CapabilityKind.Text,
        [CapabilityIds.Clock] = CapabilityKind.Text,
        [CapabilityIds.CalendarDate] = CapabilityKind.Text,
    };

    private volatile IReadOnlyDictionary<string, CapabilityKind> _live =
        new Dictionary<string, CapabilityKind>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Refresh the live id→kind map from the device read-model (called by <see cref="RefreshLoop"/> with the
    /// devices it already fetched). Devices that disagree about a shared id are resolved by majority, with a
    /// deterministic tie-break, so the answer never flickers between refreshes. Keeps the previous snapshot on
    /// an empty read (offline-first, like <see cref="ZoneCache"/>).
    /// </summary>
    public void Sync(IReadOnlyList<DbGatewayClient.DeviceSnapshot> devices)
    {
        var votes = new Dictionary<string, Dictionary<CapabilityKind, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in devices.SelectMany(d => d.Capabilities))
        {
            if (string.IsNullOrWhiteSpace(capability.Id)) continue;
            if (!Enum.TryParse<CapabilityKind>(capability.Kind, ignoreCase: true, out var kind)) continue;

            if (!votes.TryGetValue(capability.Id, out var byKind))
                votes[capability.Id] = byKind = new Dictionary<CapabilityKind, int>();
            byKind[kind] = byKind.TryGetValue(kind, out var n) ? n + 1 : 1;
        }
        if (votes.Count == 0) return; // nothing declared a kind — keep the last good map

        _live = votes.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderByDescending(x => x.Value).ThenBy(x => (int)x.Key).First().Key,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The capability's value type: as declared by the live read-model, else well-known, else Number.</summary>
    public CapabilityKind KindOf(string capabilityId) =>
        _live.TryGetValue(capabilityId, out var live) ? live : WellKnownKindOf(capabilityId);

    /// <summary>
    /// The value type of a well-known core capability id, ignoring the live read-model — the cold-start answer
    /// (and what unit tests assert against). Unknown ids default to <see cref="CapabilityKind.Number"/>.
    /// </summary>
    public static CapabilityKind WellKnownKindOf(string capabilityId) =>
        WellKnown.TryGetValue(capabilityId ?? string.Empty, out var kind) ? kind : CapabilityKind.Number;
}
