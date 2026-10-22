// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Reflection;

namespace Domovoy.Common;

/// <summary>
/// The running build's version (roadmap Epic 3K — delivery and updates).
/// <para>
/// Reads <see cref="AssemblyInformationalVersionAttribute"/> rather than
/// <c>Assembly.GetName().Version</c>: the assembly version is a four-part number that cannot carry a
/// pre-release suffix, so a dev build would report a flat <c>1.4.0.0</c> and lose the very part that
/// identifies it (<c>-dev.57+ab12cd3</c>). CI injects both — the numeric part as <c>Version</c> and the
/// full SemVer string as <c>InformationalVersion</c> — as Docker build-args (see each Dockerfile).
/// </para>
/// <para>
/// A locally built image reports <c>0.0.0</c> (the fallback in <c>Directory.Build.props</c>), which is
/// the intended signal: this binary was built here, it did not come from a release channel.
/// </para>
/// </summary>
public static class AppVersion
{
    /// <summary>Version of the entry assembly — what the process should report about itself.</summary>
    public static string Current { get; } =
        Of(Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly);

    /// <summary>
    /// Version of a specific assembly. Falls back to the assembly version, then to <c>0.0.0</c>, so
    /// callers never have to deal with nulls.
    /// </summary>
    public static string Of(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational;

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
