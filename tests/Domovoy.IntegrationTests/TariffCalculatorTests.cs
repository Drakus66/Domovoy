// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the pure tariff evaluator (roadmap Epic 3C, Etap 3) — no infrastructure. Cover the default
/// fallback, a night zone that wraps past midnight, first-match-wins ordering, the zone-name set, and the
/// forward hourly series the cheap-hours block / money endpoint consume.
/// </summary>
public sealed class TariffCalculatorTests
{
    private static TariffSettings NightRate() => new()
    {
        DefaultPrice = 6, // day rate (fallback)
        Zones = new()
        {
            new TariffZone
            {
                Name = "night", PricePerKwh = 2,
                Intervals = new() { new TariffInterval { StartMinute = 23 * 60, EndMinute = 7 * 60 } },
            },
        },
    };

    [Fact]
    public void At_NoZones_ReturnsDefault()
    {
        var s = new TariffSettings { DefaultPrice = 4.5, Zones = new() };
        var (zone, price) = TariffCalculator.At(s, 12 * 60);
        Assert.Equal(TariffCalculator.DefaultZoneName, zone);
        Assert.Equal(4.5, price, 6);
    }

    [Fact]
    public void At_NightZone_WrapsPastMidnight()
    {
        var s = NightRate();
        Assert.Equal(("night", 2.0), TariffCalculator.At(s, 0 * 60 + 30));   // 00:30 → night
        Assert.Equal(("night", 2.0), TariffCalculator.At(s, 23 * 60 + 30));  // 23:30 → night
        Assert.Equal(("night", 2.0), TariffCalculator.At(s, 6 * 60 + 59));   // 06:59 → night
        Assert.Equal((TariffCalculator.DefaultZoneName, 6.0), TariffCalculator.At(s, 7 * 60));  // 07:00 → day
        Assert.Equal((TariffCalculator.DefaultZoneName, 6.0), TariffCalculator.At(s, 12 * 60)); // noon → day
    }

    [Fact]
    public void At_FirstMatchingZoneWins()
    {
        var s = new TariffSettings
        {
            DefaultPrice = 5,
            Zones = new()
            {
                new TariffZone { Name = "peak", PricePerKwh = 8,
                    Intervals = new() { new TariffInterval { StartMinute = 7 * 60, EndMinute = 23 * 60 } } },
                new TariffZone { Name = "night", PricePerKwh = 2,
                    Intervals = new() { new TariffInterval { StartMinute = 23 * 60, EndMinute = 7 * 60 } } },
            },
        };
        Assert.Equal(("peak", 8.0), TariffCalculator.At(s, 10 * 60));
        Assert.Equal(("night", 2.0), TariffCalculator.At(s, 2 * 60));
    }

    [Fact]
    public void ZoneNames_IncludesConfiguredPlusDefault()
    {
        var s = new TariffSettings { Zones = new() { new TariffZone { Name = "peak" }, new TariffZone { Name = "night" } } };
        var names = TariffCalculator.ZoneNames(s);
        Assert.Contains("peak", names);
        Assert.Contains("night", names);
        Assert.Contains(TariffCalculator.DefaultZoneName, names);
    }

    [Fact]
    public void Forward_ProducesHourlySeries_PricedByZone()
    {
        var from = new DateTimeOffset(2026, 1, 1, 22, 0, 0, TimeSpan.Zero); // 22:00 local
        var series = TariffCalculator.Forward(NightRate(), from, 4); // 22, 23, 00, 01
        Assert.Equal(4, series.Count);
        Assert.Equal(TariffCalculator.DefaultZoneName, series[0].Zone); // 22:00 day
        Assert.Equal("night", series[1].Zone); // 23:00
        Assert.Equal("night", series[2].Zone); // 00:00
        Assert.Equal("night", series[3].Zone); // 01:00
        Assert.Equal(2.0, series[2].Price, 6);
    }
}
