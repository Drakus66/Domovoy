// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.AutomationService.Services;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Persists control-block runtime state across restarts (roadmap Epic 2Q, Phase 2). On startup it loads the
/// last snapshot of every block's state bag from the DbGateway (<c>block_state</c>); the runtime re-seeds each
/// new instance from it so latches, counters and timers survive a restart. It snapshots back periodically, and
/// dedupes on the serialized JSON so an unchanged block isn't re-written every cycle (offline-first: a gateway
/// outage just skips a snapshot, never blocks a tick).
/// </summary>
public sealed class BlockStateStore
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<BlockStateStore> _logger;

    // blockId → deserialized state, for re-seeding new instances.
    private readonly ConcurrentDictionary<string, Dictionary<string, object?>> _loaded = new();
    // blockId → last JSON we persisted, to skip unchanged snapshots.
    private readonly ConcurrentDictionary<string, string> _lastSaved = new();

    public BlockStateStore(DbGatewayClient db, ILogger<BlockStateStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Load the last persisted snapshots into the cache (called once at startup).</summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        var records = await _db.GetBlockStatesAsync(ct);
        if (records is null) return;

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.Id)) continue;
            _loaded[r.Id] = BlockStateJson.Deserialize(r.StateJson);
            _lastSaved[r.Id] = r.StateJson;
        }
        _logger.LogInformation("Loaded persisted state for {Count} block(s)", records.Count);
    }

    /// <summary>The persisted state for a block (a copy to seed a new instance), or null if none was saved.</summary>
    public Dictionary<string, object?>? Take(string blockId) =>
        _loaded.TryGetValue(blockId, out var s) ? new Dictionary<string, object?>(s) : null;

    /// <summary>Snapshot a block's live state if it changed since the last write (best-effort).</summary>
    public async Task SaveAsync(string blockId, IReadOnlyDictionary<string, object?> state, CancellationToken ct)
    {
        string json;
        try { json = BlockStateJson.Serialize(state); }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not serialize state for block {BlockId}", blockId); return; }

        if (_lastSaved.TryGetValue(blockId, out var prev) && prev == json) return; // unchanged
        _lastSaved[blockId] = json;
        await _db.SaveBlockStateAsync(blockId, json, ct);
    }

    /// <summary>Forget a removed block's persisted state.</summary>
    public async Task DeleteAsync(string blockId, CancellationToken ct)
    {
        _loaded.TryRemove(blockId, out _);
        _lastSaved.TryRemove(blockId, out _);
        await _db.DeleteBlockStateAsync(blockId, ct);
    }
}
