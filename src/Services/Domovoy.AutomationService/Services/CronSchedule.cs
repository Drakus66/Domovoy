// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Minimal 5-field cron matcher (<c>minute hour day-of-month month day-of-week</c>) evaluated at
/// per-minute resolution — enough for irrigation/lighting schedules without pulling in a cron library
/// (keeps the offline footprint small). Supports <c>*</c>, numbers, lists <c>a,b</c>, ranges
/// <c>a-b</c> and steps <c>a-b/s</c> / <c>*/s</c>. Day-of-week 0 and 7 are both Sunday.
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] _minutes;   // 0..59
    private readonly bool[] _hours;     // 0..23
    private readonly bool[] _daysOfMonth; // 1..31
    private readonly bool[] _months;    // 1..12
    private readonly bool[] _daysOfWeek; // 0..6 (Sun..Sat)
    private readonly bool _domRestricted;
    private readonly bool _dowRestricted;

    private CronSchedule(bool[] min, bool[] hour, bool[] dom, bool[] mon, bool[] dow, bool domR, bool dowR)
    {
        _minutes = min; _hours = hour; _daysOfMonth = dom; _months = mon; _daysOfWeek = dow;
        _domRestricted = domR; _dowRestricted = dowR;
    }

    public static bool TryParse(string? expression, out CronSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;

        try
        {
            var min = Field(parts[0], 0, 59, out _);
            var hour = Field(parts[1], 0, 23, out _);
            var dom = Field(parts[2], 1, 31, out var domR);
            var mon = Field(parts[3], 1, 12, out _);
            var dow = Field(parts[4], 0, 7, out var dowR); // 0..7 input, 7 normalized to 0
            // collapse Sunday=7 into 0
            var dow7 = new bool[7];
            for (var i = 0; i <= 6; i++) dow7[i] = dow[i];
            if (dow[7]) dow7[0] = true;

            schedule = new CronSchedule(min, hour, dom, mon, dow7, domR, dowR);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True if the given local time matches the schedule (to the minute).</summary>
    public bool Matches(DateTimeOffset time)
    {
        if (!_minutes[time.Minute] || !_hours[time.Hour] || !_months[time.Month]) return false;

        var domMatch = _daysOfMonth[time.Day];
        var dowMatch = _daysOfWeek[(int)time.DayOfWeek];

        // Standard cron: when both day-of-month and day-of-week are restricted, either matching is enough.
        if (_domRestricted && _dowRestricted) return domMatch || dowMatch;
        return domMatch && dowMatch;
    }

    private static bool[] Field(string spec, int min, int max, out bool restricted)
    {
        var set = new bool[max + 1];
        restricted = spec != "*";

        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var step = 1;
            var token = part;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                step = int.Parse(part[(slash + 1)..]);
                token = part[..slash];
            }

            int lo, hi;
            if (token == "*")
            {
                lo = min; hi = max;
            }
            else if (token.Contains('-'))
            {
                var r = token.Split('-');
                lo = int.Parse(r[0]); hi = int.Parse(r[1]);
            }
            else
            {
                lo = hi = int.Parse(token);
            }

            if (lo < min || hi > max || lo > hi || step < 1) throw new FormatException("cron field out of range");
            for (var v = lo; v <= hi; v += step) set[v] = true;
        }

        return set;
    }
}
