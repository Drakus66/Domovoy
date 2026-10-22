// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Update settings (roadmap Epic 3K) — a singleton document, same pattern as
/// <see cref="BackupSettings"/> and <c>site_location</c>.
/// <para>
/// Note what is <b>absent</b>: the registry, the image names, any address of the update source.
/// Those are compiled-in constants in the delivery service and deliberately not configurable. The
/// only thing an operator chooses here is the channel — which builds this house is subscribed to.
/// </para>
/// </summary>
public class UpdateSettings
{
    public const string SingletonId = "current";

    /// <summary>Stable builds from <c>master</c>.</summary>
    public const string ChannelRelease = "release";

    /// <summary>Fresh builds from <c>develop</c> — expect rough edges.</summary>
    public const string ChannelDev = "dev";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// Channel the owner chose on <c>/settings</c>, or null when they never did. Null is meaningful: the
    /// updater then follows <c>DOMOVOY_CHANNEL</c> from the install's <c>.env</c> — the channel its images were
    /// actually pulled from — and only falls back to stable without it. A hard "release" default here made
    /// that fallback dead: a house installed from <c>dev</c> polled <c>release</c> and silently saw no updates.
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>Whether the delivery service polls the registry at all. Off = fully manual.</summary>
    public bool CheckEnabled { get; set; } = true;

    public int CheckIntervalHours { get; set; } = 6;

    /// <summary>
    /// Take a backup before applying an update. On by default: the backup bundle records the exact
    /// component versions it was taken on, so "restore to how it was" stays meaningful.
    /// </summary>
    public bool BackupBeforeUpdate { get; set; } = true;

    public DateTime? LastCheckAt { get; set; }

    /// <summary><c>ok</c> or <c>error</c> for the last check.</summary>
    public string? LastCheckResult { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public static bool IsKnownChannel(string? channel) =>
        channel is ChannelRelease or ChannelDev;
}
