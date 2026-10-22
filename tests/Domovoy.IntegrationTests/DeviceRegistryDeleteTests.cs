// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Registry pruning against real Mongo (device registry page): DELETE /api/capability-devices/{id}
/// removes a device document only while it is offline — an online device is refused (409 path), a
/// missing id reports not-found. Deletion is intentionally shallow (telemetry/events age out via
/// TTL) and reversible: a later re-announce runs the ordinary discovery upsert and the device
/// re-integrates from scratch. Unique device ids isolate this data from other tests sharing the
/// infra fixture.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class DeviceRegistryDeleteTests
{
    private readonly InfraFixture _fx;
    public DeviceRegistryDeleteTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<CapabilityDeviceDocument> Devices =>
        _fx.Db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);

    private static CapabilityDeviceDocument Device(bool online) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "registry-test",
        AdapterSource = "Zigbee2Mqtt",
        IsOnline = online,
        Capabilities = new List<CapabilityDocument> { new() { Id = "on_off", Kind = "Boolean", Writable = true } },
    };

    [Fact]
    public async Task Delete_OfflineDevice_RemovesDocument()
    {
        var doc = Device(online: false);
        await Devices.InsertOneAsync(doc);

        var outcome = await CapabilityDeviceEndpoints.DeleteOfflineAsync(_fx.Db, doc.Id);

        Assert.Equal(CapabilityDeviceEndpoints.DeleteOutcome.Deleted, outcome);
        Assert.False(await Devices.Find(x => x.Id == doc.Id).AnyAsync());
    }

    [Fact]
    public async Task Delete_OnlineDevice_IsRefusedAndDocumentSurvives()
    {
        var doc = Device(online: true);
        await Devices.InsertOneAsync(doc);

        var outcome = await CapabilityDeviceEndpoints.DeleteOfflineAsync(_fx.Db, doc.Id);

        Assert.Equal(CapabilityDeviceEndpoints.DeleteOutcome.StillOnline, outcome);
        Assert.True(await Devices.Find(x => x.Id == doc.Id).AnyAsync());
    }

    [Fact]
    public async Task Delete_UnknownId_ReportsNotFound()
    {
        var outcome = await CapabilityDeviceEndpoints.DeleteOfflineAsync(_fx.Db, Guid.NewGuid().ToString());

        Assert.Equal(CapabilityDeviceEndpoints.DeleteOutcome.NotFound, outcome);
    }
}
