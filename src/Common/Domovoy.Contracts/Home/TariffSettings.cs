// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.Contracts.Home;

/// <summary>
/// Electricity tariff configuration for the platform tariff entity (roadmap Epic 3C). A single persisted
/// document the DbGateway owns and the AutomationService reads. Offline-first: static zones are edited by
/// hand (RU single/double/triple-tariff by time of day); a dynamic hourly provider (Etap 4, plugin 1C) may
/// later supply a forecast on top, but the static schedule always works with no network.
/// </summary>
public class TariffSettings
{
    /// <summary>There is only ever one tariff-settings document; this is its stable id.</summary>
    public const string SingletonId = "current";

    public string Id { get; set; } = SingletonId;

    /// <summary>Currency label; the price capability's unit is <c>"{Currency}/kWh"</c>.</summary>
    public string Currency { get; set; } = "₽";

    /// <summary>Price per kWh used when no zone interval matches the moment (flat / single-tariff default).</summary>
    public double DefaultPrice { get; set; }

    /// <summary>Tariff zones; the first whose schedule matches the local time wins.</summary>
    public List<TariffZone> Zones { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One tariff zone (e.g. peak / night / day): a price and the local time-of-day windows it is active.</summary>
public class TariffZone
{
    public string Name { get; set; } = string.Empty;

    public double PricePerKwh { get; set; }

    /// <summary>Local time-of-day windows when this zone applies; empty ⇒ the zone is always active.</summary>
    public List<TariffInterval> Intervals { get; set; } = new();
}

/// <summary>A local time-of-day window in minutes since midnight. When <see cref="EndMinute"/> ≤
/// <see cref="StartMinute"/> the window wraps past midnight (e.g. a 23:00–07:00 night rate).</summary>
public class TariffInterval
{
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

/// <summary>
/// Pure evaluation of a <see cref="TariffSettings"/> — shared by the AutomationService tariff device / cheap-hours
/// block and the DbGateway money endpoint so both price the same way (roadmap Epic 3C). No timezone logic here:
/// callers pass an already-local instant / minute-of-day.
/// </summary>
public static class TariffCalculator
{
    /// <summary>Synthetic zone name returned when no configured zone matches (the flat/default rate).</summary>
    public const string DefaultZoneName = "default";

    /// <summary>The active (zone, price) at a local minute-of-day [0,1440). First matching zone wins;
    /// none matches ⇒ (<see cref="DefaultZoneName"/>, <see cref="TariffSettings.DefaultPrice"/>).</summary>
    public static (string Zone, double Price) At(TariffSettings settings, int minuteOfDay)
    {
        foreach (var z in settings.Zones)
            if (Matches(z, minuteOfDay))
                return (ZoneName(z), z.PricePerKwh);
        return (DefaultZoneName, settings.DefaultPrice);
    }

    /// <summary>The active (zone, price) at a local instant.</summary>
    public static (string Zone, double Price) At(TariffSettings settings, DateTimeOffset local) =>
        At(settings, local.Hour * 60 + local.Minute);

    /// <summary>
    /// Forward hourly price series over <paramref name="hours"/> from <paramref name="fromLocal"/> — the input
    /// to the cheap-hours block (Etap 5) and per-hour money. Each entry is the price active at that local hour.
    /// </summary>
    public static IReadOnlyList<TariffPoint> Forward(TariffSettings settings, DateTimeOffset fromLocal, int hours)
    {
        var series = new List<TariffPoint>(Math.Max(0, hours));
        for (var i = 0; i < hours; i++)
        {
            var t = fromLocal.AddHours(i);
            var (zone, price) = At(settings, t);
            series.Add(new TariffPoint(t, zone, price));
        }
        return series;
    }

    /// <summary>Every distinct zone name in play, including the synthetic default — for the tariff-zone enum
    /// and the money breakdown.</summary>
    public static IReadOnlyList<string> ZoneNames(TariffSettings settings)
    {
        var names = new List<string>();
        foreach (var z in settings.Zones)
        {
            var n = ZoneName(z);
            if (!names.Contains(n)) names.Add(n);
        }
        if (!names.Contains(DefaultZoneName)) names.Add(DefaultZoneName);
        return names;
    }

    private static string ZoneName(TariffZone z) =>
        string.IsNullOrWhiteSpace(z.Name) ? DefaultZoneName : z.Name.Trim();

    private static bool Matches(TariffZone z, int minute)
    {
        if (z.Intervals is null || z.Intervals.Count == 0) return true; // always-on zone
        foreach (var iv in z.Intervals)
        {
            var start = Math.Clamp(iv.StartMinute, 0, 1440);
            var end = Math.Clamp(iv.EndMinute, 0, 1440);
            if (start == end) return true;                     // degenerate ⇒ full day
            var inRange = start < end
                ? minute >= start && minute < end              // same-day window
                : minute >= start || minute < end;             // wraps past midnight
            if (inRange) return true;
        }
        return false;
    }
}

/// <summary>One point of a forward tariff series (local time, active zone, price per kWh).</summary>
public readonly record struct TariffPoint(DateTimeOffset At, string Zone, double Price);
