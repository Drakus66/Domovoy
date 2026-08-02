// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Thread-safe holder of the presence layer's live state (roadmap Epic 3D): the resident roster (synced from
/// the DbGateway) and each resident's current home/away signal (driven by geofence reports). Deliberately
/// separate from <see cref="PresenceService"/> — the hosted service owns the bus/timer/announce plumbing,
/// this owns the decision logic so it is unit-testable without infrastructure. Presence is <b>not</b>
/// persisted (like the Power device's signal, and unlike the home mode): it is derived live from sources and
/// re-established as reports arrive, resetting to "unknown/away" on restart.
///
/// <para>The geofence is server-side: a reported position within <see cref="_radiusMeters"/> of the site
/// location (2K) is "home". Arrival is trusted immediately; departure waits out <see cref="_awayGraceSeconds"/>
/// (absorbing GPS jitter) before flipping to away. An explicit transition (OwnTracks enter/leave the "home"
/// region) bypasses the distance math.</para>
/// </summary>
public sealed class PresenceState
{
    private readonly object _gate = new();

    // Resident id → roster entry (name + the key a source reports under + whether it counts).
    private readonly Dictionary<string, ResidentEntry> _residents = new(StringComparer.Ordinal);
    // Resident id → live presence.
    private readonly Dictionary<string, Presence> _presence = new(StringComparer.Ordinal);

    private double _radiusMeters = 150;
    private double _awayGraceSeconds = 180;
    private double? _homeLat;
    private double? _homeLon;

    /// <summary>Roster entry mirrored from the <c>residents</c> collection.</summary>
    public sealed record ResidentEntry(string Id, string DisplayName, string? OwnTracksId, bool TrackingEnabled);

    /// <summary>A resident's live presence signal.</summary>
    public sealed record Presence(bool Home, int? Battery, DateTimeOffset? LastReportAt, DateTimeOffset? AwayPendingSince);

    /// <summary>One resident's roster entry joined with their live presence, for the status API.</summary>
    public sealed record ResidentStatus(
        string Id, string DisplayName, string? OwnTracksId, bool TrackingEnabled,
        bool Home, int? Battery, DateTimeOffset? LastReportAt);

    /// <summary>The whole presence layer at a glance: the aggregate + per-resident status.</summary>
    public sealed record Snapshot(bool AnyoneHome, int HomeCount, IReadOnlyList<ResidentStatus> Residents);

    /// <summary>Update the geofence parameters + home coordinates (from presence settings 3D + site location 2K).</summary>
    public void Configure(double radiusMeters, double awayGraceSeconds, double? homeLat, double? homeLon)
    {
        lock (_gate)
        {
            _radiusMeters = radiusMeters > 0 ? radiusMeters : 150;
            _awayGraceSeconds = Math.Max(0, awayGraceSeconds);
            _homeLat = homeLat;
            _homeLon = homeLon;
        }
    }

    /// <summary>
    /// Replace the roster with the current set from the DbGateway. Returns the ids added and removed so the
    /// caller can announce/retire virtual person devices. Presence for a departed resident is dropped; a
    /// newly-seen resident starts with no known presence (treated as away by the aggregate until reported).
    /// </summary>
    public (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) SyncRoster(IEnumerable<ResidentEntry> roster)
    {
        lock (_gate)
        {
            var next = roster.Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);

            var added = next.Keys.Where(id => !_residents.ContainsKey(id)).ToList();
            var removed = _residents.Keys.Where(id => !next.ContainsKey(id)).ToList();

            _residents.Clear();
            foreach (var kv in next) _residents[kv.Key] = kv.Value;
            foreach (var id in removed) _presence.Remove(id);

            return (added, removed);
        }
    }

    /// <summary>Current roster (snapshot copy).</summary>
    public IReadOnlyList<ResidentEntry> Roster()
    {
        lock (_gate) return _residents.Values.ToList();
    }

    /// <summary>Live presence for a resident, or null if none is known yet.</summary>
    public Presence? Get(string residentId)
    {
        lock (_gate) return _presence.TryGetValue(residentId, out var p) ? p : null;
    }

    /// <summary>
    /// Apply a geofence report. Resolves the resident by matching any of <paramref name="candidateKeys"/>
    /// (case-insensitive) against roster <c>OwnTracksId</c>s; drops the report if none match or the resident
    /// isn't tracked. Returns the affected resident id when their <b>home/away</b> value changed (so the
    /// caller republishes), else null. Battery-only changes update state but don't count as a presence change.
    /// </summary>
    public string? ApplyReport(
        IReadOnlyList<string> candidateKeys, double? lat, double? lon, bool? explicitPresent,
        int? battery, DateTimeOffset now)
    {
        lock (_gate)
        {
            var resident = ResolveByKeys(candidateKeys);
            if (resident is null || !resident.TrackingEnabled) return null;

            var previous = _presence.TryGetValue(resident.Id, out var p) ? p : null;
            var wasHome = previous?.Home ?? false;

            bool home;
            DateTimeOffset? awayPendingSince;
            if (explicitPresent is bool ep)
            {
                // A trusted transition (OwnTracks entered/left the home region) flips immediately, both ways —
                // no distance math and no grace: the phone already crossed the boundary.
                home = ep;
                awayPendingSince = null;
            }
            else
            {
                // Geofence the coordinates; with no resolvable position, keep the prior value.
                var inside = TryGeofence(lat, lon, out var geo) ? geo : wasHome;
                if (inside)
                {
                    home = true;                 // arrival is trusted immediately
                    awayPendingSince = null;
                }
                else if (!wasHome)
                {
                    home = false;                // already away — stays away
                    awayPendingSince = null;
                }
                else
                {
                    // Was home, now reads outside: start (or continue) the grace window; flip once it elapses.
                    var since = previous?.AwayPendingSince ?? now;
                    if ((now - since).TotalSeconds >= _awayGraceSeconds)
                    {
                        home = false;
                        awayPendingSince = null;
                    }
                    else
                    {
                        home = true;
                        awayPendingSince = since;
                    }
                }
            }

            _presence[resident.Id] = new Presence(home, battery ?? previous?.Battery, now, awayPendingSince);
            return home != wasHome ? resident.Id : null;
        }
    }

    /// <summary>
    /// Resolve pending away-transitions whose grace window has elapsed (driven by the periodic tick, since a
    /// resident who leaves and stops reporting sends no further events). Returns the ids that flipped to away.
    /// </summary>
    public IReadOnlyList<string> EvaluatePending(DateTimeOffset now)
    {
        lock (_gate)
        {
            var flipped = new List<string>();
            foreach (var id in _presence.Keys.ToList())
            {
                var p = _presence[id];
                if (p is { Home: true, AwayPendingSince: { } since }
                    && (now - since).TotalSeconds >= _awayGraceSeconds)
                {
                    _presence[id] = p with { Home = false, AwayPendingSince = null };
                    flipped.Add(id);
                }
            }
            return flipped;
        }
    }

    /// <summary>Aggregate over tracked residents: how many are home, and whether anyone is. A resident with no
    /// known presence counts as away, so a fresh install reads "nobody home" rather than unknown.</summary>
    public (bool AnyoneHome, int HomeCount) Aggregate()
    {
        lock (_gate)
        {
            var count = _residents.Values
                .Where(r => r.TrackingEnabled)
                .Count(r => _presence.TryGetValue(r.Id, out var p) && p.Home);
            return (count > 0, count);
        }
    }

    /// <summary>Full roster + presence + aggregate snapshot for the status API (roadmap Epic 3D).</summary>
    public Snapshot GetSnapshot()
    {
        lock (_gate)
        {
            var residents = _residents.Values
                .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(r =>
                {
                    _presence.TryGetValue(r.Id, out var p);
                    return new ResidentStatus(
                        r.Id, r.DisplayName, r.OwnTracksId, r.TrackingEnabled,
                        p?.Home ?? false, p?.Battery, p?.LastReportAt);
                })
                .ToList();
            var count = residents.Count(r => r.TrackingEnabled && r.Home);
            return new Snapshot(count > 0, count, residents);
        }
    }

    private ResidentEntry? ResolveByKeys(IReadOnlyList<string> candidateKeys)
    {
        foreach (var entry in _residents.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.OwnTracksId)) continue;
            foreach (var key in candidateKeys)
                if (string.Equals(entry.OwnTracksId, key, StringComparison.OrdinalIgnoreCase))
                    return entry;
        }
        return null;
    }

    private bool TryGeofence(double? lat, double? lon, out bool inside)
    {
        inside = false;
        if (lat is not double la || lon is not double lo || _homeLat is not double hla || _homeLon is not double hlo)
            return false;
        inside = Haversine(hla, hlo, la, lo) <= _radiusMeters;
        return true;
    }

    /// <summary>Great-circle distance in metres between two lat/lon points (WGS-84 mean radius).</summary>
    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; // metres
        double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
