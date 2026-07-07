// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.AutomationService.Configuration;
using Domovoy.Contracts.Automations;

using Microsoft.Extensions.Options;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Holds the active rule set the engine evaluates: <b>safety-floor</b> rules loaded once from a local
/// file (protected, always present — they run even when the DB/UI is down) merged with <b>user</b>
/// rules refreshed from the DbGateway. On a gateway failure the last good user rules are kept, so the
/// engine degrades gracefully (offline-first, roadmap principle 2).
/// </summary>
public sealed class RuleStore
{
    private readonly DbGatewayClient _db;
    private readonly AutomationOptions _options;
    private readonly ILogger<RuleStore> _logger;

    private List<AutomationRule> _safety = new();
    private List<AutomationRule> _user = new();
    private volatile IReadOnlyList<AutomationRule> _rules = Array.Empty<AutomationRule>();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RuleStore(DbGatewayClient db, IOptions<AutomationOptions> options, ILogger<RuleStore> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>All currently active rules (safety floor first, then user rules).</summary>
    public IReadOnlyList<AutomationRule> Rules => _rules;

    /// <summary>Load the local safety-floor rules once at startup (protected, non-disableable).</summary>
    public void LoadSafetyRules()
    {
        var path = _options.SafetyRulesPath;
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("No safety-rules file at {Path} — safety floor is empty", path);
                _safety = new();
            }
            else
            {
                var rules = JsonSerializer.Deserialize<List<AutomationRule>>(File.ReadAllText(path), Json) ?? new();
                foreach (var r in rules)
                {
                    r.IsProtected = true;                  // enforce: file rules are the protected floor
                    r.Status = RuleStatus.Active;          // always active
                    if (string.IsNullOrEmpty(r.Id)) r.Id = $"safety:{r.Name}";
                }
                _safety = rules;
                _logger.LogInformation("Loaded {Count} safety-floor rule(s)", _safety.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load safety rules from {Path}", path);
            _safety = new();
        }
        Recompose();
    }

    /// <summary>Refresh user rules from the DbGateway; keeps the last good set on failure.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        var user = await _db.GetUserRulesAsync(ct);
        if (user is not null)
        {
            _user = user;
            _logger.LogDebug("Refreshed {Count} user rule(s)", _user.Count);
        }
        Recompose();
    }

    private void Recompose() => _rules = _safety.Concat(_user).ToList();
}
