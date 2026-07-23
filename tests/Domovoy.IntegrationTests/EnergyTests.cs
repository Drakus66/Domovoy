// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Home;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Energy accounting against real Mongo (roadmap Epic 3C / 3C-D): per-device kWh over a window from the
/// cumulative <c>energy</c> counter, and the double-count guard (the <c>mains</c> role and the per-device
/// accounting toggle). Unique device ids isolate this data; total assertions are self-consistency checks so a
/// shared fixture can't skew them.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class EnergyTests
{
    private readonly InfraFixture _fx;
    public EnergyTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<SensorReading> Readings =>
        _fx.Db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection);
    private IMongoCollection<CapabilityDeviceDocument> Devices =>
        _fx.Db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);
    private IMongoCollection<TariffSettings> Tariffs =>
        _fx.Db.GetCollection<TariffSettings>(SettingsEndpoints.TariffCollection);

    private static CapabilityDeviceDocument EnergyDevice(
        string id, string name, string? role = null, bool? track = null) => new()
    {
        Id = id, Name = name, ZoneId = "z", AdapterSource = "Zigbee2Mqtt",
        EnergyProfile = role is null && track is null ? null : new EnergyProfile { Role = role, Track = track },
        AutoArchetype = DeviceArchetypes.EnergyMeter,
        Capabilities = new() { new CapabilityDocument { Id = "energy", Kind = "Number", Unit = "kWh" } },
    };

    private static SensorReading Energy(string id, double value, DateTime ts) => new()
    {
        Timestamp = ts,
        Meta = new TelemetryMeta { DeviceId = id, ZoneId = "z", CapabilityId = "energy" },
        Value = value,
    };

    [Fact]
    public async Task Consumption_SumsDeltaPerDevice_HonorsRoles_AndRanks()
    {
        var boiler = Guid.NewGuid().ToString();
        var fridge = Guid.NewGuid().ToString();
        var mains = Guid.NewGuid().ToString();
        var rig = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await Devices.InsertManyAsync(new[]
        {
            EnergyDevice(boiler, "Boiler"),
            EnergyDevice(fridge, "Fridge"),
            EnergyDevice(mains, "Mains", role: EnergyEndpoints.MainsRole),
            EnergyDevice(rig, "Test rig", track: false),
        });

        // Cumulative counters climbing within one bucket → consumption = last − first.
        await Readings.InsertManyAsync(new[]
        {
            Energy(boiler, 100, now.AddSeconds(-6)), Energy(boiler, 105, now),   // 5 kWh
            Energy(fridge, 10, now.AddSeconds(-6)), Energy(fridge, 11, now),     // 1 kWh
            Energy(mains, 1000, now.AddSeconds(-6)), Energy(mains, 1007, now),   // 7 kWh (grand total only)
            Energy(rig, 3, now.AddSeconds(-6)), Energy(rig, 20, now),            // 17 kWh (ignored)
        });

        var res = await EnergyEndpoints.ConsumptionAsync(_fx.Db, from: null, to: null, bucket: "hour");

        // Per-device consumption (isolated by unique ids).
        Assert.Equal(5, res.Devices.Single(d => d.DeviceId == boiler).Kwh, 3);
        Assert.Equal(1, res.Devices.Single(d => d.DeviceId == fridge).Kwh, 3);
        Assert.Equal(7, res.Devices.Single(d => d.DeviceId == mains).Kwh, 3);

        // Accounting turned off ⇒ the device is not in the result at all (its 17 kWh count nowhere).
        Assert.DoesNotContain(res.Devices, d => d.DeviceId == rig);

        // Roles are surfaced for grouping.
        Assert.Null(res.Devices.Single(d => d.DeviceId == boiler).EnergyRole);
        Assert.Equal(EnergyEndpoints.MainsRole, res.Devices.Single(d => d.DeviceId == mains).EnergyRole);

        // Top Consumers: ranked by kWh descending (mains 7 > boiler 5 > fridge 1).
        var mine = res.Devices.Where(d => d.DeviceId == boiler || d.DeviceId == fridge
            || d.DeviceId == mains).ToList();
        Assert.Equal(new[] { mains, boiler, fridge }, mine.Select(d => d.DeviceId));

        // Totals honor the double-count guard — checked as a self-consistency invariant so a shared fixture
        // (other energy devices) can't skew the assertion: consumer total excludes mains + excluded.
        Assert.Equal(res.Devices.Where(d => d.EnergyRole is null).Sum(d => d.Kwh), res.ConsumerTotalKwh, 3);
        Assert.Equal(res.Devices.Where(d => d.EnergyRole == EnergyEndpoints.MainsRole).Sum(d => d.Kwh),
            res.MainsTotalKwh, 3);
    }

    [Fact]
    public async Task Cost_FlatTariff_PricesKwhByDefaultRate()
    {
        // Flat tariff: every hour prices at DefaultPrice (no zones) → cost = kWh × price. This invariant is
        // robust to other consumer devices sharing the fixture, so no exact-total dependency.
        await Tariffs.ReplaceOneAsync(
            x => x.Id == TariffSettings.SingletonId,
            new TariffSettings { Currency = "₽", DefaultPrice = 5, Zones = new() },
            new ReplaceOptions { IsUpsert = true });

        var dev = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        await Devices.InsertOneAsync(EnergyDevice(dev, "Meter"));
        await Readings.InsertManyAsync(new[]
        {
            Energy(dev, 100, now.AddSeconds(-6)), Energy(dev, 104, now), // 4 kWh
        });

        var res = await EnergyEndpoints.CostAsync(_fx.Db, from: null, to: null);

        Assert.Equal("₽", res.Currency);
        Assert.True(res.TotalKwh >= 4, $"expected ≥4 kWh, got {res.TotalKwh}");
        // cost = kWh × 5 (flat), within a rounding tolerance; all consumption in the synthetic default zone.
        Assert.True(Math.Abs(res.TotalCost - res.TotalKwh * 5) < 0.05,
            $"cost {res.TotalCost} vs kWh {res.TotalKwh} × 5");
        Assert.All(res.Zones, z => Assert.Equal(TariffCalculator.DefaultZoneName, z.Zone));
        Assert.Equal(res.TotalCost, res.Zones.Sum(z => z.Cost), 2);
    }
}
