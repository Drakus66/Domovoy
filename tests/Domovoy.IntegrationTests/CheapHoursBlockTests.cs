// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Blocks;
using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Unit tests for the Epic 3C cheap-hours block (Etap 5), driven by the in-memory <see cref="FakeBlockCtx"/>
/// with a controllable clock over a real <see cref="TariffContext"/> (evaluated in UTC via a default
/// <see cref="SiteContext"/>). Cover the cheap/expensive decision, the enable gate, the price cap, the
/// zero-hours guard and plunge mode.
/// </summary>
public sealed class CheapHoursBlockTests
{
    // Night rate 23:00–07:00 @2, day @6 (default). Evaluated in UTC (SiteContext defaults to UTC).
    private static TariffContext NightTariff()
    {
        var ctx = new TariffContext(new SiteContext());
        ctx.Update(new TariffSettings
        {
            DefaultPrice = 6,
            Zones = new()
            {
                new TariffZone
                {
                    Name = "night", PricePerKwh = 2,
                    Intervals = new() { new TariffInterval { StartMinute = 23 * 60, EndMinute = 7 * 60 } },
                },
            },
        });
        return ctx;
    }

    private static TariffContext FlatTariff(double price)
    {
        var ctx = new TariffContext(new SiteContext());
        ctx.Update(new TariffSettings { DefaultPrice = price, Zones = new() });
        return ctx;
    }

    private static FakeBlockCtx At(int utcHour) =>
        new() { Now = new DateTimeOffset(2026, 1, 1, utcHour, 0, 0, TimeSpan.Zero) };

    [Fact]
    public void CheapHour_TurnsOn()
    {
        var ctx = At(2); // 02:00 → night (price 2), among the cheapest hours
        new CheapHoursBlock(NightTariff()).Tick(ctx);
        Assert.True(ctx.GetBool(CapabilityIds.OnOff));
    }

    [Fact]
    public void ExpensiveHour_StaysOff()
    {
        var ctx = At(12); // noon → day (price 6), not among the cheapest
        new CheapHoursBlock(NightTariff()).Tick(ctx);
        Assert.False(ctx.GetBool(CapabilityIds.OnOff));
    }

    [Fact]
    public void EnableFalse_ForcesOff_EvenInCheapHour()
    {
        var ctx = At(2);
        ctx.Inputs["enable"] = false;
        new CheapHoursBlock(NightTariff()).Tick(ctx);
        Assert.False(ctx.GetBool(CapabilityIds.OnOff));
    }

    [Fact]
    public void MaxPrice_ExcludesHoursAboveCap()
    {
        var ctx = At(2);
        ctx.Params["maxPrice"] = 1; // night price 2 > 1 → not eligible
        new CheapHoursBlock(NightTariff()).Tick(ctx);
        Assert.False(ctx.GetBool(CapabilityIds.OnOff));
    }

    [Fact]
    public void CheapestZero_NeverRuns()
    {
        var ctx = At(2);
        ctx.Params["cheapestHours"] = 0;
        new CheapHoursBlock(NightTariff()).Tick(ctx);
        Assert.False(ctx.GetBool(CapabilityIds.OnOff));
    }

    [Fact]
    public void PlungeOnly_RunsOnlyWhenNegative()
    {
        var onCtx = At(12);
        onCtx.Options["plungeOnly"] = "true";
        new CheapHoursBlock(FlatTariff(-0.5)).Tick(onCtx);
        Assert.True(onCtx.GetBool(CapabilityIds.OnOff)); // price −0.5 < 0

        var offCtx = At(12);
        offCtx.Options["plungeOnly"] = "true";
        new CheapHoursBlock(FlatTariff(3)).Tick(offCtx);
        Assert.False(offCtx.GetBool(CapabilityIds.OnOff)); // price 3 ≥ 0
    }
}
