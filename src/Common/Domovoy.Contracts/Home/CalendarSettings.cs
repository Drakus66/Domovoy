// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// Calendar configuration for the system Calendar sensor (roadmap Epic 2L): which weekdays count as the
/// weekend, and which dates are holidays. A single persisted document the DbGateway owns and the
/// AutomationService reads. Offline-first: both are edited by hand on the Settings page; an optional online
/// import (public-holiday provider) only pre-fills the holiday list when there is a network.
/// </summary>
public class CalendarSettings
{
    /// <summary>There is only ever one calendar-settings document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// Weekdays counted as weekend, as <see cref="System.DayOfWeek"/> integers (0=Sunday … 6=Saturday).
    /// Defaults to Saturday+Sunday.
    /// </summary>
    public List<int> WeekendDays { get; set; } = new() { 6, 0 };

    /// <summary>Holiday dates in local <c>yyyy-MM-dd</c> form.</summary>
    public List<string> Holidays { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
