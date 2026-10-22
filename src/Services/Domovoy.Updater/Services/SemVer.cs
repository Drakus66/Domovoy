// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Updater.Services;

/// <summary>
/// Ordering for component versions (roadmap Epic 3K). Just enough SemVer to answer "which of these
/// is newer": numeric parts compare left to right, and a build with a pre-release suffix sorts
/// below the same version without one (<c>1.4.0-dev.57</c> &lt; <c>1.4.0</c>).
/// <para>
/// Anything unparseable sorts lowest rather than throwing — a malformed tag in the registry must
/// never be picked as "the newest", and must never break the update check either.
/// </para>
/// </summary>
public sealed class SemVerComparer : IComparer<string>
{
    public static readonly SemVerComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var (xNums, xPre) = Split(x);
        var (yNums, yPre) = Split(y);

        for (var i = 0; i < Math.Max(xNums.Length, yNums.Length); i++)
        {
            var a = i < xNums.Length ? xNums[i] : 0;
            var b = i < yNums.Length ? yNums[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        // Отсутствие pre-release суффикса — «старше»: 1.4.0 новее, чем 1.4.0-dev.57.
        if (xPre is null && yPre is null) return 0;
        if (xPre is null) return 1;
        if (yPre is null) return -1;
        return string.CompareOrdinal(xPre, yPre);
    }

    private static (int[] Numbers, string? PreRelease) Split(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return (Array.Empty<int>(), "");

        // Метаданные сборки (+sha) на порядок не влияют — SemVer это прямо предписывает.
        var core = version.Split('+', 2)[0];
        var parts = core.Split('-', 2);
        var numeric = parts[0]
            .Split('.')
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .ToArray();

        return (numeric, parts.Length > 1 ? parts[1] : null);
    }

    public static bool IsNewer(string candidate, string current) =>
        Instance.Compare(candidate, current) > 0;
}
