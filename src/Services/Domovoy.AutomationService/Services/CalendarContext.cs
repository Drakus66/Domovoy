// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Live calendar configuration (roadmap Epic 2L) for the system Calendar sensor: which weekdays are the
/// weekend and which local dates are holidays. Refreshed from the persisted <c>CalendarSettings</c> (owned
/// by the DbGateway) by <see cref="RefreshLoop"/>. Held as one immutable snapshot behind a volatile
/// reference so a concurrent tick always reads a consistent set. Defaults to Sat+Sun, no holidays.
/// </summary>
public sealed class CalendarContext
{
    private volatile Snapshot _snapshot = new(
        new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday },
        new HashSet<string>(StringComparer.Ordinal));

    public bool IsWeekend(DayOfWeek day) => _snapshot.Weekend.Contains(day);

    /// <summary>True when <paramref name="localDate"/> (yyyy-MM-dd) is a configured holiday.</summary>
    public bool IsHoliday(DateOnly localDate) =>
        _snapshot.Holidays.Contains(localDate.ToString("yyyy-MM-dd"));

    /// <summary>Replace the configuration. Weekend ints are <see cref="DayOfWeek"/> values (0=Sunday…6=Saturday).</summary>
    public void Update(IEnumerable<int>? weekendDays, IEnumerable<string>? holidays)
    {
        var weekend = (weekendDays ?? Array.Empty<int>())
            .Where(d => d is >= 0 and <= 6).Select(d => (DayOfWeek)d).ToHashSet();
        if (weekend.Count == 0) { weekend.Add(DayOfWeek.Saturday); weekend.Add(DayOfWeek.Sunday); }

        var holidaySet = (holidays ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        _snapshot = new Snapshot(weekend, holidaySet);
    }

    private sealed record Snapshot(HashSet<DayOfWeek> Weekend, HashSet<string> Holidays);
}
