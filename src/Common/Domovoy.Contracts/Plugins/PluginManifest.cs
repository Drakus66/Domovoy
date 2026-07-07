// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Plugins;

/// <summary>
/// Declarative manifest of an integration plugin (roadmap Epic 1C). A plugin is an <b>out-of-process</b>
/// integration that connects to the bus itself and speaks the capability contract (<c>Domovoy.Contracts</c>)
/// — so a new device adapter is added by dropping a manifest, with no core recompile. The supervisor reads
/// the manifest to gate activation on host resources, launch/stop the process, and record what the plugin
/// provides/needs. Discovered as <c>plugin.json</c> in a plugin's folder under the plugins root.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Stable plugin id (folder name is used if omitted).</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary><c>process</c> (out-of-process, the supported kind) — <c>inprocess</c> ALC is a later step.</summary>
    public string Kind { get; set; } = "process";

    /// <summary>Executable/command to launch for a process plugin (resolved relative to the plugin folder).</summary>
    public string? Command { get; set; }

    /// <summary>Command-line arguments for the process.</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Capabilities the plugin provides (well-known capability ids) — documentation/discovery.</summary>
    public List<string> ProvidedCapabilities { get; set; } = new();

    /// <summary>Bus event types the plugin subscribes to.</summary>
    public List<string> Subscriptions { get; set; } = new();

    /// <summary>Bus command types the plugin accepts.</summary>
    public List<string> Commands { get; set; } = new();

    /// <summary>Permissions the plugin requests (recorded now; enforced with local auth in Phase 2).</summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>Host resources the plugin needs — the supervisor activates it only when these are satisfiable.</summary>
    public ResourceRequirements Resources { get; set; } = new();

    /// <summary>Start automatically when resources allow and it is enabled.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>Operator switch independent of resource gating (disabled plugins never run).</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Resources a plugin declares it needs (roadmap principle 3 — resource-aware modularity). The supervisor
/// compares these against the host so heavy/cloud plugins activate only where they can actually run,
/// otherwise the system stays on its base functionality.
/// </summary>
public sealed class ResourceRequirements
{
    /// <summary>Minimum logical CPU cores.</summary>
    public int CpuCores { get; set; }

    /// <summary>Minimum available memory, MB.</summary>
    public int MemoryMb { get; set; }

    /// <summary>Requires a GPU.</summary>
    public bool Gpu { get; set; }

    /// <summary>Requires internet access (cloud integrations).</summary>
    public bool Internet { get; set; }
}
