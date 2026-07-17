// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Automations;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Holds the active rule set the engine evaluates: user rules refreshed from the DbGateway. On a gateway
/// failure the last good set is kept, so the engine degrades gracefully (offline-first, roadmap principle 2).
/// There is no local hardcoded rule floor — every rule the engine runs is a user automation that exists in
/// the DB and is visible/editable/deletable from the WebUI (so run-history entries always resolve to a real
/// automation).
/// </summary>
public sealed class RuleStore
{
    private readonly DbGatewayClient _db;
    private readonly ILogger<RuleStore> _logger;

    private volatile IReadOnlyList<AutomationRule> _rules = Array.Empty<AutomationRule>();

    public RuleStore(DbGatewayClient db, ILogger<RuleStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>All currently active rules (from the DbGateway).</summary>
    public IReadOnlyList<AutomationRule> Rules => _rules;

    /// <summary>Refresh user rules from the DbGateway; keeps the last good set on failure.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var user = await _db.GetUserRulesAsync(ct);
        if (user is not null)
        {
            _rules = user;
            _logger.LogDebug("Refreshed {Count} rule(s)", user.Count);
        }
    }
}
