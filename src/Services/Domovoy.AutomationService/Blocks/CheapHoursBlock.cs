// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// The "cheap hours" control block (roadmap Epic 3C) — turns on during the N cheapest hours of a look-ahead
/// window by tariff price, to shift a deferrable load (boiler, EV charge, irrigation, pool pump) into cheap
/// electricity. It reads the forward price series from the live <see cref="TariffContext"/> (injected, like the
/// sun gate closes over the sun calculator), not a bound input, since a scalar-per-tick port can't carry a
/// forward curve. Bind its <c>on_off</c> output to the load. Plunge pricing (negative prices) is a special case.
/// </summary>
public sealed class CheapHoursType : IBlockType
{
    private readonly TariffContext _tariff;

    public CheapHoursType(TariffContext tariff) => _tariff = tariff;

    public string TypeId => "cheap_hours";
    public string Category => BlockCategories.Time;
    public string Title => "Cheap hours (tariff)";
    public string Description => "Turns on during the N cheapest hours of the window by tariff price — shifts a deferrable load (boiler / EV / irrigation) into cheap electricity. Bind the on_off output to the load.";

    public IReadOnlyList<BlockPortSpec> Inputs { get; } = new[]
    {
        new BlockPortSpec("enable", CapabilityKind.Boolean, "Optional gate — when bound and false, forces off", Optional: true),
    };

    public IReadOnlyList<Capability> Outputs { get; } = new[]
    {
        WellKnownCapabilities.OnOff(writable: false),
    };

    public IReadOnlyList<BlockParamSpec> Params { get; } = new[]
    {
        new BlockParamSpec("windowHours", 24, "h", 1, 48, "Look-ahead window the hours are ranked over"),
        new BlockParamSpec("cheapestHours", 4, "h", 0, 48, "How many of the cheapest hours in the window run"),
        new BlockParamSpec("maxPrice", 0, null, null, null, "Only run at or below this price per kWh (0 = no cap)"),
    };

    public IReadOnlyList<BlockOptionSpec> Options { get; } = new[]
    {
        new BlockOptionSpec("plungeOnly", BlockOptionKind.Bool, "false", "Only run during negative (plunge) prices"),
    };

    public IBlock Create() => new CheapHoursBlock(_tariff);
}

public sealed class CheapHoursBlock : IBlock
{
    private readonly TariffContext _tariff;

    public CheapHoursBlock(TariffContext tariff) => _tariff = tariff;

    public void Tick(IBlockContext ctx)
    {
        // Live gate: an unbound enable is "armed"; bound-and-false forces off immediately.
        if (GenericBlockConventions.ReadBool(ctx, "enable") == false)
        {
            ctx.Emit(CapabilityIds.OnOff, false);
            return;
        }

        var window = (int)Math.Clamp(ctx.Param("windowHours", 24), 1, 48);
        var cheapest = (int)Math.Clamp(ctx.Param("cheapestHours", 4), 0, 48);
        var maxPrice = ctx.Param("maxPrice", 0);
        var plungeOnly = GenericBlockConventions.OptBool(ctx, "plungeOnly", false);

        var series = _tariff.Forward(ctx.Now, window);
        if (series.Count == 0)
        {
            ctx.Emit(CapabilityIds.OnOff, false);
            return;
        }

        // Decide once per local hour and hold it, so the load runs for whole cheap hours without chatter at the
        // hour boundary as the ranking window slides.
        var hourKey = series[0].At.ToString("yyyy-MM-ddTHH");
        bool on;
        if (hourKey == ctx.GetState<string>("hour"))
        {
            on = ctx.GetState<bool>("on");
        }
        else
        {
            on = Decide(series, cheapest, maxPrice, plungeOnly);
            ctx.SetState("hour", hourKey);
            ctx.SetState("on", on);
        }

        ctx.Emit(CapabilityIds.OnOff, on);
    }

    /// <summary>
    /// Is the current hour (<c>series[0]</c>) one to run? Plunge mode → only when the price is negative. Otherwise
    /// the hour runs when fewer than <paramref name="cheapest"/> window hours are strictly cheaper (ties keep it
    /// on), within an optional <paramref name="maxPrice"/> cap.
    /// </summary>
    private static bool Decide(IReadOnlyList<TariffPoint> series, int cheapest, double maxPrice, bool plungeOnly)
    {
        var current = series[0].Price;
        if (plungeOnly) return current < 0;
        if (maxPrice > 0 && current > maxPrice) return false;
        if (cheapest <= 0) return false;

        var pool = maxPrice > 0 ? series.Where(p => p.Price <= maxPrice) : series;
        return pool.Count(p => p.Price < current) < cheapest;
    }
}
