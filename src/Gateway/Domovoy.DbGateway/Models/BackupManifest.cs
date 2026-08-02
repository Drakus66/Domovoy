// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.DbGateway.Models;

/// <summary>
/// <c>manifest.json</c> inside a backup bundle (roadmap Epic 3A). The manifest is the restore driver:
/// only collections listed here are dropped and re-created, and time-series collections carry their
/// creation options so a restore on a clean stack rebuilds them with the right shape.
/// </summary>
public class BackupManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public DateTime CreatedAt { get; set; }

    public string Database { get; set; } = "";

    /// <summary><c>manual</c>, <c>scheduled</c> or <c>upload</c> — provenance shown in the UI list.</summary>
    public string Reason { get; set; } = "manual";

    public string? AppVersion { get; set; }

    public List<BackupCollectionInfo> Collections { get; set; } = new();

    /// <summary>Plugin settings files (Epic 1C <c>.settings/{id}.json</c>) included in the bundle.</summary>
    public List<string> PluginSettings { get; set; } = new();

    /// <summary>Extra host directories archived under <c>extras/</c> (configs; restored to a staging dir).</summary>
    public List<string> Extras { get; set; } = new();

    /// <summary>
    /// Which build of the system this bundle was taken on (roadmap Epic 3K). Null when the delivery
    /// service was unreachable — a backup is never blocked on it.
    /// </summary>
    public BackupRuntimeInfo? Runtime { get; set; }
}

/// <summary>
/// The exact set of component versions a backup was taken on (roadmap Epic 3K).
/// <para>
/// Recording the full declared compatibility, not just version strings, is what lets a restore say
/// something useful: not merely "these versions differ", but "this bundle predates a breaking bus
/// change, restoring it onto the current build will not work".
/// </para>
/// </summary>
public class BackupRuntimeInfo
{
    /// <summary>Channel the installation was subscribed to — <c>dev</c> or <c>release</c>.</summary>
    public string? Channel { get; set; }

    /// <summary>Applied deployment-topology version.</summary>
    public int? TopologyVersion { get; set; }

    public List<BackupComponentInfo> Components { get; set; } = new();
}

public class BackupComponentInfo
{
    public string Name { get; set; } = "";

    public string? Version { get; set; }

    /// <summary>Content-addressed image identity — the only reference that cannot be re-pointed.</summary>
    public string? Digest { get; set; }

    /// <summary>Declared compatibility (<c>bus</c>/<c>provides</c>/<c>requires</c>), verbatim.</summary>
    public object? Deps { get; set; }
}

public class BackupCollectionInfo
{
    public string Name { get; set; } = "";

    public long Documents { get; set; }

    /// <summary>Present when the collection is a MongoDB time-series collection.</summary>
    public BackupTimeSeriesInfo? TimeSeries { get; set; }
}

public class BackupTimeSeriesInfo
{
    public string TimeField { get; set; } = "Timestamp";

    public string? MetaField { get; set; }

    /// <summary><c>seconds</c> | <c>minutes</c> | <c>hours</c>.</summary>
    public string? Granularity { get; set; }
}
