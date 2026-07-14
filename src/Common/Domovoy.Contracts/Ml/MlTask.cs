// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Ml;

/// <summary>
/// A user-managed ML training task (Epic 2P): <i>what</i> to learn (the target capability), <i>from what</i>
/// (history window, per-zone scoping), and <i>within which limits</i> (clamps). Stored in the <c>ml_tasks</c>
/// collection and edited from the WebUI ML hub; the AutomationService trainer iterates all enabled tasks and
/// registers the resulting models per (kind, target, scope) in <c>ml_models</c>. Replaces the single
/// env-configured <c>Automation.TrainCapability</c> — options remain only as seed defaults.
/// Tasks join to their models by <see cref="TargetCapability"/> (unique across tasks), so pre-existing
/// registered models need no migration.
/// </summary>
public class MlTask
{
    /// <summary>Stable task id (GUID string; the seeded default task uses <c>"default"</c>). Server-assigned on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name; defaults to the target capability.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Capability the task learns to predict (e.g. <c>temperature</c>). Unique across tasks.</summary>
    public string TargetCapability { get; set; } = string.Empty;

    /// <summary>Disabled tasks are not trained; already-registered models keep serving (removal = delete versions).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>History window (days) pulled from telemetry / the event-log for training.</summary>
    public int WindowDays { get; set; } = 30;

    /// <summary>Minimum samples required to train (cold-start guard).</summary>
    public int MinSamples { get; set; } = 20;

    /// <summary>How often to retrain automatically.</summary>
    public int TrainIntervalHours { get; set; } = 24;

    /// <summary>Also fit per-zone-kind and (where they earn it) per-zone models (Epic 2I).</summary>
    public bool TrainZoneModels { get; set; } = true;

    /// <summary>Holdout margin a per-zone candidate must beat its fallback by to be registered (Epic 2I).</summary>
    public double ZonePromotionMargin { get; set; } = 0.25;

    /// <summary>
    /// Soft clamp applied to the model's predictions of a numeric target (Epic 2P). Distinct from the
    /// governor's static safety floor, which stays the hard non-negotiable bound. Null = unclamped.
    /// </summary>
    public double? ClampMin { get; set; }
    public double? ClampMax { get; set; }

    /// <summary>Model versions kept per (kind, target, scope) line; older ones are pruned on register.</summary>
    public int KeepLastVersions { get; set; } = 10;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Outcome of the most recent training attempt. Written only by the trainer (via the dedicated status
    /// endpoint) — user edits of the task never touch it, and vice versa.
    /// </summary>
    public MlTaskStatus? Status { get; set; }
}

/// <summary>Outcome of the latest training attempt for a task — the "why is it (not) training" signal for the UI.</summary>
public sealed class MlTaskStatus
{
    /// <summary>When the last training <i>attempt</i> ran (successful or not); drives the per-task schedule.</summary>
    public DateTime? LastTrainAt { get; set; }

    public bool LastTrainOk { get; set; }

    /// <summary>Human-readable outcome, e.g. <c>ok (+2 zone models)</c> or <c>not enough data (12/20)</c>.</summary>
    public string? LastMessage { get; set; }

    /// <summary>Training samples the last successful run fitted on (global scope).</summary>
    public int LastSampleCount { get; set; }

    /// <summary>How many scope models (global + zone kinds + zones) the last run registered.</summary>
    public int LastRegisteredScopes { get; set; }
}
