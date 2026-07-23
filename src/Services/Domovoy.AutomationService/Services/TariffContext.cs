// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Live tariff configuration (roadmap Epic 3C), refreshed from the persisted <see cref="TariffSettings"/>
/// (owned by the DbGateway) by <see cref="RefreshLoop"/>. Held as one immutable snapshot behind a volatile
/// reference so a concurrent tick always reads a consistent set. Converts to the site's local time (via
/// <see cref="SiteContext"/>) because a static tariff schedule is expressed in local wall-clock. Used by the
/// <see cref="TariffService"/> virtual device and (Etap 5) the cheap-hours block. Defaults to a flat 0-price
/// tariff until real settings load — offline-first.
/// </summary>
public sealed class TariffContext
{
    private readonly SiteContext _site;
    private volatile TariffSettings _settings = new();

    public TariffContext(SiteContext site) => _site = site;

    public TariffSettings Settings => _settings;

    public string Currency => _settings.Currency;

    /// <summary>Replace the tariff configuration (ignores a null, keeping the last-known set — offline-first).</summary>
    public void Update(TariffSettings? settings)
    {
        if (settings is not null) _settings = settings;
    }

    /// <summary>Active (zone, price) at a UTC instant, evaluated in the site's local time.</summary>
    public (string Zone, double Price) At(DateTimeOffset utc) =>
        TariffCalculator.At(_settings, LocalOf(utc));

    /// <summary>Forward hourly price series from a UTC instant (cheap-hours block / money), aligned to local hours.</summary>
    public IReadOnlyList<TariffPoint> Forward(DateTimeOffset fromUtc, int hours) =>
        TariffCalculator.Forward(_settings, LocalOf(fromUtc), hours);

    /// <summary>Distinct zone names (incl. the synthetic default) for the tariff-zone enum.</summary>
    public IReadOnlyList<string> ZoneNames() => TariffCalculator.ZoneNames(_settings);

    private DateTimeOffset LocalOf(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _site.TimeZone);
}
