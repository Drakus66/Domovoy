// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Holds the global variable configs + live values (roadmap Epic 3E), refreshed from the DbGateway by the
/// <see cref="RefreshLoop"/> — the variable analogue of <see cref="Blocks.BlockStore"/>. Keeps the last good
/// set on a gateway failure (offline-first).
/// </summary>
public sealed class VariableStore
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<VariableStore> _logger;
    private volatile IReadOnlyList<GlobalVariable> _variables = Array.Empty<GlobalVariable>();

    public VariableStore(DbGatewayClient db, ILogger<VariableStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyList<GlobalVariable> Variables => _variables;

    public async Task RefreshAsync(CancellationToken ct)
    {
        var variables = await _db.GetVariablesAsync(ct);
        if (variables is not null)
        {
            _variables = variables;
            _logger.LogDebug("Refreshed {Count} global variable(s)", variables.Count);
        }
    }
}
