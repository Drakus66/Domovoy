// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Home;
using Domovoy.DbGateway.Endpoints;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Persistence round-trip against real Mongo (roadmap Epic 3D): the <see cref="Resident"/> roster documents
/// and the singleton <see cref="PresenceSettings"/>, mirroring how <c>LoadManagementPersistenceTests</c>
/// exercises the Epic 3C-LM collections directly.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class PresencePersistenceTests
{
    private readonly InfraFixture _fx;
    public PresencePersistenceTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<Resident> Residents =>
        _fx.Db.GetCollection<Resident>(ResidentsEndpoints.Collection);
    private IMongoCollection<PresenceSettings> Settings =>
        _fx.Db.GetCollection<PresenceSettings>(SettingsEndpoints.PresenceCollection);

    [Fact]
    public async Task Resident_RoundTrips_IncludingUserLinkAndSource()
    {
        var id = Guid.NewGuid().ToString();
        var resident = new Resident
        {
            Id = id,
            DisplayName = "Аня",
            UserId = "u-123",
            OwnTracksId = "anya",
            TrackingEnabled = true,
        };

        await Residents.InsertOneAsync(resident);
        var loaded = await Residents.Find(x => x.Id == id).FirstOrDefaultAsync();

        Assert.NotNull(loaded);
        Assert.Equal("Аня", loaded!.DisplayName);
        Assert.Equal("u-123", loaded.UserId);
        Assert.Equal("anya", loaded.OwnTracksId);
        Assert.True(loaded.TrackingEnabled);

        await Residents.DeleteOneAsync(x => x.Id == id);
    }

    [Fact]
    public async Task PresenceSettings_RoundTrips_Singleton()
    {
        var settings = new PresenceSettings
        {
            HomeRadiusMeters = 200,
            AwayGraceSeconds = 240,
            OwnTracksToken = "s3cr3t",
        };

        await Settings.ReplaceOneAsync(
            x => x.Id == PresenceSettings.SingletonId, settings, new ReplaceOptions { IsUpsert = true });

        var loaded = await Settings.Find(x => x.Id == PresenceSettings.SingletonId).FirstOrDefaultAsync();

        Assert.NotNull(loaded);
        Assert.Equal(PresenceSettings.SingletonId, loaded!.Id);
        Assert.Equal(200, loaded.HomeRadiusMeters);
        Assert.Equal(240, loaded.AwayGraceSeconds);
        Assert.Equal("s3cr3t", loaded.OwnTracksToken);
    }
}
