// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Ml;

/// <summary>
/// Runtime switches for the intelligence (ML) layer (roadmap Epic 3I). A single persisted document the
/// DbGateway owns and the AutomationService re-reads each refresh cycle, so toggling takes effect without a
/// restart (mirrors <see cref="Domovoy.Contracts.Home.LoadManagementSettings"/>). Both switches default ON —
/// the layer is opt-out, not opt-in — but a household that does not want proactive intelligence can silence
/// the proposers, or the whole layer, from here.
/// </summary>
public class MlSettings
{
    /// <summary>There is only ever one ML-settings document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// Master switch for the whole ML layer. False ⇒ no training runs, no model is served for inference
    /// (governors then auto-demote to Shadow via the existing "no model" path), and no proposer queues
    /// anything. Default on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Switch for just the proactive proposers (rule heuristic 2C, ML-task suggester 2P, pattern discovery
    /// 2F). False ⇒ they never queue proposals on their own, but training and serving continue and the manual
    /// "scan now" buttons still work (an explicit human action is never spam). Default on. Ignored while
    /// <see cref="Enabled"/> is false (the whole layer is off).
    /// </summary>
    public bool ProposalsEnabled { get; set; } = true;

    /// <summary>
    /// Cold-start gate: the periodic proposers stay silent until the event-log holds at least this many days
    /// of history (measured from the earliest recorded event). Stops a fresh install proposing on a handful of
    /// minutes of data — the ML journal (Epic 3I) shows the accrual progress meanwhile. Default 7; 0 disables
    /// the gate.
    /// </summary>
    public int MinHistoryDays { get; set; } = 7;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
