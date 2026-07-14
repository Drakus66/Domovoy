// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.CommutePlugin;

/// <summary>
/// The plugin's pure decision maths, factored out of <see cref="Services.CommutePlanner"/> so it can be unit
/// tested without a bus or a host: turning a wall-clock deadline into a concrete instant, and choosing the
/// adaptive recompute cadence from how close the key moment is.
/// </summary>
internal static class CommuteMath
{
    /// <summary>
    /// Resolves an "arrive by <c>HH:mm</c>" string to a concrete local instant relative to
    /// <paramref name="nowLocal"/>. A slot that has already passed today rolls to tomorrow (so a deadline set
    /// the night before is understood as the next morning). Null for missing/malformed input.
    /// </summary>
    public static DateTimeOffset? ParseDeadline(string? hhmm, DateTimeOffset nowLocal)
    {
        if (string.IsNullOrWhiteSpace(hhmm) || !TimeOnly.TryParse(hhmm, out var time))
            return null;

        var candidate = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day,
            time.Hour, time.Minute, 0, nowLocal.Offset);
        if (candidate < nowLocal) candidate = candidate.AddDays(1);
        return candidate;
    }

    /// <summary>
    /// Recompute cadence in seconds: rare when there is lots of slack before the key moment
    /// (<paramref name="keyMinutes"/>), tightening to <paramref name="minSeconds"/> as it approaches.
    /// </summary>
    public static int AdaptiveIntervalSeconds(double keyMinutes, int minSeconds)
    {
        var seconds = keyMinutes switch
        {
            > 60 => 300,
            > 30 => 120,
            > 10 => 60,
            _ => minSeconds,
        };
        return Math.Max(minSeconds, seconds);
    }
}
