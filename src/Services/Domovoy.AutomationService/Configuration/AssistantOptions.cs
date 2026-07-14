// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Configuration;

/// <summary>
/// Configuration for the natural-language assistant extension point (roadmap Epic 2H). Bound from the
/// <c>Assistant</c> config section. The capability is a deliberate <b>stub</b> — an interface plus a feature
/// flag so a real natural-language backend can be plugged in later (as a config-selected implementation or an
/// out-of-process plugin, Epic 1C) without touching the call sites. Off by default: development runs on a laptop
/// with no model, and the assistant is never in the control path (it only helps author rules and explain actions
/// on top of the deterministic engine + 1F attribution).
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>Master switch. While false, the connector reports unavailable and every request returns a graceful stub.</summary>
    public bool Enabled { get; set; }

    /// <summary>Name of the backend a future implementation would use (documentation only until one is wired).</summary>
    public string Provider { get; set; } = string.Empty;
}
