// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// A single automation rule run (roadmap Epic 1A). Persisted by the EventInterceptor from
/// <c>AutomationTriggeredV1</c> into the <c>auto_history</c> collection. Lets the UI show what fired,
/// whether conditions held and whether actions succeeded — the basis for explainability (Epic 1F).
/// </summary>
public class AutoHistory
{
    [BsonId]
    public ObjectId Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;

    /// <summary>Whether all conditions held (false ⇒ triggered but skipped).</summary>
    public bool ConditionsMet { get; set; }

    /// <summary>Whether the actions executed without error.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable description of what triggered the rule.</summary>
    public string TriggerSummary { get; set; } = string.Empty;

    public int ActionsExecuted { get; set; }

    public string? Detail { get; set; }
}
