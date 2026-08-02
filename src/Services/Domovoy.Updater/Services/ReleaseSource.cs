// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Updater.Model;

namespace Domovoy.Updater.Services;

/// <summary>
/// Where updates come from (roadmap Epic 3K) — <b>compiled in on purpose</b>.
/// <para>
/// The registry and the package names are constants, not configuration: there is no environment
/// override and no field in the UI. The only thing an operator chooses is the
/// <see cref="UpdateChannels">channel</see>. Pointing an installation at a different source
/// therefore takes editing this file and rebuilding — not flipping a variable.
/// </para>
/// <para>
/// Honest scope: this stops accident and misconfiguration, not a determined fork — and it is not
/// meant to. AGPL explicitly allows forking, and a fork <i>should</i> ship from its own channel.
/// What it must not be able to do is pass its images off as ours to <i>this</i> installation, and
/// that boundary is drawn cryptographically by a signed release manifest — see
/// <see cref="IReleaseTrustPolicy"/>, which is the seam kept ready for it.
/// </para>
/// </summary>
public static class ReleaseSource
{
    /// <summary>Registry namespace holding every Domovoy package.</summary>
    public const string Registry = "ghcr.io";

    /// <summary>Owner/namespace inside the registry (lowercase — the registry requires it).</summary>
    public const string Namespace = "drakus66";

    public const string ImagePrefix = "domovoy-";

    /// <summary>Package carrying the deployment topology bundle.</summary>
    public const string TopologyComponent = "topology";

    /// <summary>
    /// The components this system is made of, in <b>safe recreation order</b>: providers before
    /// consumers, the two things the browser talks to (api-gateway, webui) late, and the updater
    /// itself last — it can only be replaced by a throwaway agent after everything else is done.
    /// </summary>
    public static readonly IReadOnlyList<string> Components = new[]
    {
        "unified-device-service",
        "connectivity-service",
        "db-gateway",
        "automation-service",
        "plugin-supervisor",
        "api-gateway",
        "webui",
        "updater",
    };

    /// <summary>The updater's own component name — excluded from the normal recreate loop.</summary>
    public const string SelfComponent = "updater";

    /// <summary>Repository path inside the registry, e.g. <c>drakus66/domovoy-db-gateway</c>.</summary>
    public static string RepositoryOf(string component) =>
        $"{Namespace}/{ImagePrefix}{component}";

    /// <summary>Fully qualified image reference without a tag.</summary>
    public static string ImageOf(string component) =>
        $"{Registry}/{RepositoryOf(component)}";

    /// <summary>True when an image reference belongs to this (the compiled-in) source.</summary>
    public static bool IsOurs(string? imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference)) return false;
        return imageReference.StartsWith($"{Registry}/{Namespace}/{ImagePrefix}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Component name for a container name, or null when the container is not ours.</summary>
    public static string? ComponentOfContainer(string container) => container switch
    {
        "domovoy-updater" => SelfComponent,
        _ => Components.Contains(container) ? container : null,
    };

    /// <summary>Container name for a component — the inverse of <see cref="ComponentOfContainer"/>.</summary>
    public static string ContainerOf(string component) =>
        component == SelfComponent ? "domovoy-updater" : component;

    /// <summary>Recreation rank; lower goes first. Unknown components sort just before the updater.</summary>
    public static int OrderOf(string component)
    {
        var i = Components.ToList().IndexOf(component);
        return i < 0 ? Components.Count - 1 : i;
    }
}
