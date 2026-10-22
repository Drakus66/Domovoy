// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Home;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Persistence round-trip against real Mongo (roadmap Epic 3C-LM): the singleton
/// <see cref="LoadManagementSettings"/> document and the per-device <see cref="LoadSheddingProfile"/>
/// nested doc (incl. the mode-tier/priority dictionaries), mirroring how <c>EnergyTests</c> exercises the
/// Epic 3C collections directly rather than through the minimal-API HTTP layer.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class LoadManagementPersistenceTests
{
    private readonly InfraFixture _fx;
    public LoadManagementPersistenceTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<LoadManagementSettings> Settings =>
        _fx.Db.GetCollection<LoadManagementSettings>(SettingsEndpoints.LoadManagementCollection);
    private IMongoCollection<CapabilityDeviceDocument> Devices =>
        _fx.Db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);

    [Fact]
    public async Task LoadManagementSettings_RoundTrips_EnabledAndBudgets()
    {
        var settings = new LoadManagementSettings
        {
            Enabled = true,
            RestoreMarginWatts = 150,
            MinDwellSeconds = 90,
            Budgets = new()
            {
                new PowerBudget { PowerSource = WellKnownPowerSources.Grid, LimitWatts = 5000 },
                new PowerBudget { PowerSource = WellKnownPowerSources.Battery, LimitWatts = 1200 },
            },
        };

        await Settings.ReplaceOneAsync(
            x => x.Id == LoadManagementSettings.SingletonId, settings, new ReplaceOptions { IsUpsert = true });

        var loaded = await Settings.Find(x => x.Id == LoadManagementSettings.SingletonId).FirstOrDefaultAsync();

        Assert.NotNull(loaded);
        Assert.True(loaded!.Enabled);
        Assert.Equal(150, loaded.RestoreMarginWatts);
        Assert.Equal(90, loaded.MinDwellSeconds);
        Assert.Equal(2, loaded.Budgets.Count);
        Assert.Equal(5000, loaded.Budgets.Single(b => b.PowerSource == WellKnownPowerSources.Grid).LimitWatts);
        Assert.Equal(1200, loaded.Budgets.Single(b => b.PowerSource == WellKnownPowerSources.Battery).LimitWatts);
    }

    [Fact]
    public async Task LoadSheddingProfile_RoundTrips_OnDeviceDocument_IncludingModeMatrix()
    {
        var id = Guid.NewGuid().ToString();
        var device = new CapabilityDeviceDocument
        {
            Id = id,
            Name = "Water heater",
            ZoneId = "z",
            AdapterSource = "Zigbee2Mqtt",
            AutoArchetype = DeviceArchetypes.Unknown,
            // The watts live in the energy profile now (Epic 3C-D) — the shedding profile only says how.
            EnergyProfile = new EnergyProfile { Track = true, MaxPowerW = 2000 },
            LoadShedding = new LoadSheddingProfile
            {
                Enabled = true,
                Protected = false,
                ControlCapabilityId = "on_off",
                Curtailable = false,
                ModeTier = new() { ["Home"] = "sheddable", ["Away"] = "critical" },
                ModePriority = new() { ["Home"] = 3 },
            },
        };

        await Devices.InsertOneAsync(device);
        var loaded = await Devices.Find(x => x.Id == id).FirstOrDefaultAsync();

        Assert.NotNull(loaded?.LoadShedding);
        var profile = loaded!.LoadShedding!;
        Assert.True(profile.Enabled);
        Assert.Equal(2000, loaded.EnergyProfile?.MaxPowerW);
        Assert.Equal("sheddable", profile.ModeTier["Home"]);
        Assert.Equal("critical", profile.ModeTier["Away"]);
        Assert.Equal(3, profile.ModePriority["Home"]);
    }

    [Fact]
    public async Task LoadShedding_Clears_WhenSetToNull()
    {
        var id = Guid.NewGuid().ToString();
        var device = new CapabilityDeviceDocument
        {
            Id = id, Name = "Heater", ZoneId = "z", AdapterSource = "Zigbee2Mqtt",
            AutoArchetype = DeviceArchetypes.Unknown,
            EnergyProfile = new EnergyProfile { Track = true, MaxPowerW = 500 },
            LoadShedding = new LoadSheddingProfile { Enabled = true },
        };
        await Devices.InsertOneAsync(device);

        var update = Builders<CapabilityDeviceDocument>.Update.Set(x => x.LoadShedding, null);
        await Devices.UpdateOneAsync(x => x.Id == id, update);

        var loaded = await Devices.Find(x => x.Id == id).FirstOrDefaultAsync();
        Assert.Null(loaded?.LoadShedding);
    }
}
