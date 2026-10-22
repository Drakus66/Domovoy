// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.Contracts.Scenes;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Holds the scenes the engine can activate (roadmap Epic 3B), refreshed from the DbGateway alongside
/// rules and blocks. The <c>scene</c> rule action stores only a scene id, so at run time
/// <see cref="ActionExecutor"/> resolves it here to the scene's device targets. On a gateway failure the
/// last good set is kept (offline-first, roadmap principle 2), mirroring <see cref="RuleStore"/>.
/// </summary>
public sealed class SceneStore
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<SceneStore> _logger;

    private volatile IReadOnlyDictionary<string, Scene> _byId =
        new Dictionary<string, Scene>(StringComparer.Ordinal);

    public SceneStore(DbGatewayClient db, ILogger<SceneStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Resolve a scene by id, or null if it is unknown.</summary>
    public Scene? Get(string id) =>
        !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var scene) ? scene : null;

    /// <summary>Refresh scenes from the DbGateway; keeps the last good set on failure.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var scenes = await _db.GetScenesAsync(ct);
        if (scenes is null) return;

        var byId = new Dictionary<string, Scene>(StringComparer.Ordinal);
        foreach (var scene in scenes)
            if (!string.IsNullOrEmpty(scene.Id)) byId[scene.Id] = scene;

        _byId = byId;
        _logger.LogDebug("Refreshed {Count} scene(s)", byId.Count);
    }
}
