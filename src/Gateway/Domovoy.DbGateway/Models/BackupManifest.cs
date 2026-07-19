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
