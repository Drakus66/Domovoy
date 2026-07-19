// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.DbGateway.Config;

/// <summary>
/// Backup platform options (roadmap Epic 3A). Bound from the <c>Backup</c> config section. These are
/// deploy-time paths (volumes); the user-facing schedule/retention lives in the runtime-editable
/// <c>backup_settings</c> document, not here.
/// </summary>
public class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>
    /// Where bundles are written. Relative paths resolve against the content root; in compose this is a
    /// host bind-mount (<c>./data/backups</c>) so bundles survive the container.
    /// </summary>
    public string Directory { get; set; } = "backups";

    /// <summary>
    /// The plugin settings directory (Epic 1C <c>{plugins-root}/.settings</c>) when mounted into this
    /// container. Empty/missing — plugin settings are simply not bundled.
    /// </summary>
    public string? PluginSettingsDirectory { get; set; }

    /// <summary>
    /// Extra host directories to archive into the bundle (service configs, safety rules, …). Backup-only:
    /// a restore extracts them to a staging folder next to the bundles for manual placement, never over
    /// live files.
    /// </summary>
    public List<string> IncludeDirectories { get; set; } = new();
}
