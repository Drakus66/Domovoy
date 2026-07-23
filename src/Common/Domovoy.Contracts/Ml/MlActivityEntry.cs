// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Ml;

/// <summary>
/// One entry in the ML-activity journal (roadmap Epic 3I) — the visible "pulse" of the proactive layer, kept
/// OUT of the operational logs (so a household that ignores ML never has it clutter the Activity Center) and
/// IN one place (the ML page) for those who care. Written by the trainer and the three proposers after each
/// cycle; TTL-retained in the <c>ml_activity</c> collection. Structured, not prose: the UI localizes a
/// one-liner from <see cref="Reason"/> + <see cref="Metrics"/>, so "history 9 of 21 days" reads in the user's
/// language.
/// </summary>
public class MlActivityEntry
{
    /// <summary>Stable id (GUID string). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When the cycle finished (UTC). Doubles as the TTL anchor.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Which contour produced this (see <see cref="MlActivitySources"/>).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Outcome category the UI colours by (see <see cref="MlActivityOutcomes"/>).</summary>
    public string Outcome { get; set; } = MlActivityOutcomes.Idle;

    /// <summary>
    /// Machine-readable reason slug the UI localizes (<c>trained</c>, <c>created</c>, <c>no_patterns</c>,
    /// <c>history_immature</c>, <c>layer_disabled</c>, <c>proposals_disabled</c>, <c>data_unavailable</c>,
    /// <c>error</c>, …). Unknown slugs fall back to <see cref="Note"/>.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>English fallback note for slugs the UI does not localize (also the human-readable summary).</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Structured counters behind the entry (<c>candidates</c>, <c>created</c>, <c>samples</c>,
    /// <c>hypotheses</c>, <c>windowDays</c>, <c>historyDays</c>, <c>requiredDays</c>, <c>models</c>, …) so the
    /// UI renders a localized line. Numeric-only by design, mirroring <see cref="Proposals.Proposal.Evidence"/>.
    /// </summary>
    public Dictionary<string, double>? Metrics { get; set; }
}

/// <summary>The contours that write ML-activity entries (roadmap Epic 3I).</summary>
public static class MlActivitySources
{
    public const string Trainer = "trainer";
    public const string Discovery = "discovery";
    public const string RuleSuggester = "rule_suggester";
    public const string MlTaskSuggester = "ml_task_suggester";
}

/// <summary>Outcome buckets for an ML-activity entry — the UI colours the timeline by these.</summary>
public static class MlActivityOutcomes
{
    /// <summary>Produced something — models trained, or proposals created.</summary>
    public const string Ok = "ok";

    /// <summary>Ran cleanly but found nothing new (the honest "quiet" state).</summary>
    public const string Idle = "idle";

    /// <summary>Gated off before doing work — layer/proposers disabled, or history still immature.</summary>
    public const string Skipped = "skipped";

    /// <summary>The cycle failed (gateway unreachable, exception).</summary>
    public const string Error = "error";
}
