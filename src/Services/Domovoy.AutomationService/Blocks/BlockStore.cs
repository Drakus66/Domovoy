// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Blocks;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Holds the control-block instance configs (roadmap Epic 1H), refreshed from the DbGateway by the
/// <see cref="RefreshLoop"/> — the block analogue of <see cref="RuleStore"/>. Keeps the last good set on
/// a gateway failure (offline-first).
/// </summary>
public sealed class BlockStore
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<BlockStore> _logger;
    private volatile IReadOnlyList<ControlBlock> _blocks = Array.Empty<ControlBlock>();

    public BlockStore(DbGatewayClient db, ILogger<BlockStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyList<ControlBlock> Blocks => _blocks;

    public async Task RefreshAsync(CancellationToken ct)
    {
        var blocks = await _db.GetBlocksAsync(ct);
        if (blocks is not null)
        {
            _blocks = blocks;
            _logger.LogDebug("Refreshed {Count} control block(s)", blocks.Count);
        }
    }
}
