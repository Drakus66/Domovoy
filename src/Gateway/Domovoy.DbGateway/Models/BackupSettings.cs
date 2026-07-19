// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Backup schedule/retention settings (roadmap Epic 3A) — a singleton document, same pattern as
/// <c>site_location</c>/<c>calendar_settings</c>. Scheduling is a daily local-time run (the site timezone
/// from Epic 2K when set), retention is "keep the newest N bundles". The Last* fields are the status the
/// UI shows ("last backup at …, ok/error") and the scheduler's idempotency marker.
/// </summary>
public class BackupSettings
{
    public const string SingletonId = "current";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    /// <summary>Automatic daily backups. On by default — a fresh install starts protecting itself.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Daily run time, <c>HH:mm</c>, in the site-local timezone (Epic 2K) or server-local without one.</summary>
    public string Time { get; set; } = "03:30";

    /// <summary>How many bundles to keep; older ones are deleted after each successful backup.</summary>
    public int KeepCount { get; set; } = 7;

    public DateTime? LastRunAt { get; set; }

    /// <summary><c>ok</c> or <c>error</c> for the last run (manual or scheduled).</summary>
    public string? LastResult { get; set; }

    public string? LastError { get; set; }

    /// <summary>Bundle file name the last successful run produced.</summary>
    public string? LastFile { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
