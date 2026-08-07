// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Live site context beyond geometry (roadmap Epic 2L): the installation's timezone, refreshed from the
/// persisted <c>SiteLocation</c> (2K) by <see cref="RefreshLoop"/>. The system Sun sensor uses it to render
/// today's sunrise/sunset in local time. The coordinates themselves stay in <see cref="SunCalculator"/>;
/// this holds only what the calculator doesn't. Defaults to UTC until a location with a timezone is loaded.
/// </summary>
public sealed class SiteContext
{
    private volatile TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>
    /// Resolve and store the timezone from an IANA id (e.g. <c>Europe/Moscow</c>). Unknown/blank ids fall
    /// back to UTC so the sensor keeps working offline. Returns true when the zone actually changed.
    /// </summary>
    public bool SetTimeZone(string? ianaId)
    {
        var resolved = Resolve(ianaId);
        if (string.Equals(resolved.Id, _timeZone.Id, StringComparison.Ordinal)) return false;
        _timeZone = resolved;
        return true;
    }

    private static TimeZoneInfo Resolve(string? ianaId) => SiteTimeZone.Resolve(ianaId);
}
