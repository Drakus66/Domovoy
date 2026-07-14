// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Plugins;
using Domovoy.PluginSupervisor.Configuration;

namespace Domovoy.PluginSupervisor.Resources;

/// <summary>
/// A snapshot of what the host can offer plugins (roadmap principle 3 — resource-aware modularity).
/// CPU/RAM come from the runtime; GPU/internet are declared via config (they can't be reliably probed in
/// a container without extra dependencies). Compared against each manifest's
/// <see cref="ResourceRequirements"/> to decide whether a plugin may activate.
/// </summary>
public sealed record HostResources(int CpuCores, int MemoryMb, bool Gpu, bool Internet)
{
    public static HostResources Detect(SupervisorOptions options)
    {
        var memoryMb = options.MemoryMbOverride > 0
            ? options.MemoryMbOverride
            : (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));

        return new HostResources(
            CpuCores: Environment.ProcessorCount,
            MemoryMb: memoryMb,
            Gpu: options.GpuAvailable,
            Internet: options.InternetAvailable);
    }

    /// <summary>Can this host satisfy a plugin's requirements? Returns the first unmet reason if not.</summary>
    public (bool Ok, string? Reason) CanSatisfy(ResourceRequirements need)
    {
        if (need.CpuCores > CpuCores)
            return (false, $"needs {need.CpuCores} cores, host has {CpuCores}");
        if (need.MemoryMb > MemoryMb)
            return (false, $"needs {need.MemoryMb} MB RAM, host has {MemoryMb} MB");
        if (need.Gpu && !Gpu)
            return (false, "needs a GPU, none available");
        if (need.Internet && !Internet)
            return (false, "needs internet, none available");
        return (true, null);
    }
}
