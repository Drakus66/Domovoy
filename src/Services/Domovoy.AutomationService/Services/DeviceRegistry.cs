// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// In-memory view of devices the rule engine evaluates against: current capability values (kept live
/// from the bus) plus each device's zone (from the DbGateway read-model, since adapters announce no
/// zone). Values are normalized (<see cref="ValueOps.Normalize"/>) on ingest so comparisons are uniform.
/// </summary>
public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<Guid, Entry> _devices = new();

    private sealed class Entry
    {
        public string ZoneId = string.Empty;
        public ConcurrentDictionary<string, object?> State = new();
    }

    private Entry GetOrAdd(Guid id) => _devices.GetOrAdd(id, _ => new Entry());

    public void SetZone(Guid id, string? zoneId) => GetOrAdd(id).ZoneId = zoneId ?? string.Empty;

    /// <summary>Update a live value; returns the previous (normalized) value for delta-aware triggers.</summary>
    public object? SetValue(Guid id, string capabilityId, object? value)
    {
        var entry = GetOrAdd(id);
        entry.State.TryGetValue(capabilityId, out var old);
        entry.State[capabilityId] = ValueOps.Normalize(value);
        return old;
    }

    /// <summary>Seed a value only if not already present (don't clobber newer live state with a snapshot).</summary>
    public void SeedValue(Guid id, string capabilityId, object? value) =>
        GetOrAdd(id).State.TryAdd(capabilityId, ValueOps.Normalize(value));

    public object? GetValue(Guid id, string capabilityId) =>
        _devices.TryGetValue(id, out var e) && e.State.TryGetValue(capabilityId, out var v) ? v : null;

    public string GetZone(Guid id) => _devices.TryGetValue(id, out var e) ? e.ZoneId : string.Empty;

    public IEnumerable<Guid> DevicesInZone(string zoneId) =>
        _devices.Where(kv => kv.Value.ZoneId == zoneId).Select(kv => kv.Key);
}
