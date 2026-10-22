// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Narrative;
using Domovoy.DbGateway.Models;
using Domovoy.Narrative;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Turns the append-only event-log into language-neutral diary <see cref="Beat"/>s (roadmap Epic 2N,
/// Phase 1). It reads <c>state_change</c> records from <c>device_events</c> over a window and enriches each
/// with the effective device archetype (2D) and zone name/kind (P0-3) from the read-models, the persona
/// (from TriggerSource / System adapter) and the transition (old→new). System-sensor transitions
/// (<c>is_dark</c>…) become impersonal beats — the raw material the <see cref="SceneBuilder"/> adopts as a
/// scene's cause. Mode-change narration is deferred to Phase 2.
/// </summary>
public sealed class DiaryMiner
{
    private readonly IMongoDatabase _db;

    public DiaryMiner(IMongoDatabase db) => _db = db;

    public async Task<List<Beat>> MineAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var b = Builders<DeviceEventLog>.Filter;
        var filter = b.And(
            b.Gte(x => x.Timestamp, fromUtc),
            b.Lte(x => x.Timestamp, toUtc),
            b.Eq(x => x.Meta.Kind, EventKinds.StateChange));

        var events = await _db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection)
            .Find(filter).SortBy(x => x.Timestamp).ToListAsync(ct);
        if (events.Count == 0) return new List<Beat>();

        var devices = await LoadDevicesAsync(events.Select(e => e.Meta.DeviceId), ct);
        var zones = await LoadZonesAsync(events.Select(e => e.Meta.ZoneId), ct);

        var beats = new List<Beat>(events.Count);
        foreach (var e in events)
        {
            devices.TryGetValue(e.Meta.DeviceId, out var dev);
            var archetype = dev is null
                ? DeviceArchetypes.Unknown
                : (string.IsNullOrEmpty(dev.Archetype) ? dev.AutoArchetype : dev.Archetype!);

            string? zoneName = null, zoneKind = null;
            if (!string.IsNullOrEmpty(e.Meta.ZoneId) && zones.TryGetValue(e.Meta.ZoneId, out var z))
            {
                zoneName = z.Name;
                zoneKind = z.Kind;
            }

            beats.Add(new Beat
            {
                Timestamp = e.Timestamp,
                Actor = PersonaResolver.Resolve(e.TriggerSource, dev?.AdapterSource, archetype, e.CapabilityId),
                // Only System virtual sensors (2L) may be narrated as a scene's cause; a hardware telemetry
                // tick that merely preceded a rule must not be claimed as its reason.
                CauseCandidate = string.Equals(
                    dev?.AdapterSource, PersonaResolver.SystemAdapterSource, StringComparison.OrdinalIgnoreCase),
                ArchetypeKey = archetype,
                CapabilityId = e.CapabilityId,
                Transition = TransitionResolver.Derive(e.CapabilityId, e.OldValue, e.NewValue),
                DeviceId = e.Meta.DeviceId,
                DeviceRef = e.Meta.DeviceId,
                ZoneId = e.Meta.ZoneId,
                ZoneName = zoneName,
                ZoneKind = zoneKind,
                OldValue = e.OldValue,
                NewValue = e.NewValue,
                RuleId = e.RuleId,
                Mode = e.Mode,
            });
        }

        return beats;
    }

    private async Task<Dictionary<string, CapabilityDeviceDocument>> LoadDevicesAsync(
        IEnumerable<string> deviceIds, CancellationToken ct)
    {
        var ids = deviceIds.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, CapabilityDeviceDocument>();
        var docs = await _db.GetCollection<CapabilityDeviceDocument>("capability_devices")
            .Find(Builders<CapabilityDeviceDocument>.Filter.In(x => x.Id, ids)).ToListAsync(ct);
        return docs.ToDictionary(d => d.Id, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, Zone>> LoadZonesAsync(IEnumerable<string> zoneIds, CancellationToken ct)
    {
        var ids = zoneIds.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, Zone>();
        var docs = await _db.GetCollection<Zone>("zones")
            .Find(Builders<Zone>.Filter.In(x => x.Id, ids)).ToListAsync(ct);
        return docs.ToDictionary(z => z.Id, StringComparer.Ordinal);
    }
}
