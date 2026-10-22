// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;
using Domovoy.DbGateway.Stores;
using Domovoy.DbGateway.Stores.Mongo;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// Batch history read-path against real Mongo (dashboard fill), exercised through the Epic 3H storage-abstraction
/// seam (<see cref="ITelemetryStore"/>): the multi-series telemetry aggregation and per-device latest-event
/// provenance. Verifies one round-trip returns correctly split-per-series buckets and the most-recent event per
/// device. Going through the store (not the endpoint's old statics) is exactly what Ф1 asks for — the same test
/// will run against a future PostgreSQL store. Unique device ids isolate this data from other infra-fixture tests.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class HistoryBatchTests
{
    private readonly InfraFixture _fx;
    public HistoryBatchTests(InfraFixture fx) => _fx = fx;

    private ITelemetryStore Store => new MongoTelemetryStore(_fx.Db);

    private IMongoCollection<SensorReading> Readings =>
        _fx.Db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection);
    private IMongoCollection<DeviceEventLog> Events =>
        _fx.Db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection);

    private static SensorReading Reading(string deviceId, string cap, double value, DateTime ts) => new()
    {
        Timestamp = ts,
        Meta = new TelemetryMeta { DeviceId = deviceId, ZoneId = "z", CapabilityId = cap },
        Value = value,
    };

    [Fact]
    public async Task AggregateBatch_SplitsBucketsPerSeries_AndHonorsAgg()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await Readings.InsertManyAsync(new[]
        {
            Reading(a, "temperature", 20, now.AddMinutes(-9)),
            Reading(a, "temperature", 22, now.AddMinutes(-6)),
            Reading(a, "temperature", 24, now.AddMinutes(-3)),
            Reading(b, "power", 100, now.AddMinutes(-4)),
            Reading(b, "power", 300, now.AddMinutes(-2)),
        });

        var avg = await Store.AggregateBatchAsync(
            new[]
            {
                new SeriesSpec(a, "temperature"),
                new SeriesSpec(b, "power"),
                new SeriesSpec(a, "humidity"), // no samples → empty series
            },
            null, null, "hour", "avg", null, default);

        Assert.Equal(3, avg.Count);

        var tempA = avg.Single(s => s.DeviceId == a && s.CapabilityId == "temperature");
        var lastA = Assert.Single(tempA.Buckets); // three readings, one clock-hour → one bucket
        Assert.Equal(22, lastA.Avg, 3);
        Assert.Equal(20, lastA.Min, 3);
        Assert.Equal(24, lastA.Max, 3);
        Assert.Equal(22, lastA.Value, 3); // agg=avg
        Assert.Equal(3, lastA.Count);

        var powerB = avg.Single(s => s.DeviceId == b && s.CapabilityId == "power");
        Assert.Equal(200, powerB.Buckets.Sum(x => x.Avg) / powerB.Buckets.Count, 3);

        var empty = avg.Single(s => s.DeviceId == a && s.CapabilityId == "humidity");
        Assert.Empty(empty.Buckets);

        // agg=max returns the bucket max as Value.
        var max = await Store.AggregateBatchAsync(
            new[] { new SeriesSpec(a, "temperature") }, null, null, "hour", "max", null, default);
        Assert.Equal(24, max.Single().Buckets.Last().Value, 3);
    }

    [Fact]
    public async Task AggregateBatch_ComputesSumAndDelta_ForCumulativeCounter()
    {
        var d = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        // A cumulative energy counter climbing within one bucket (Epic 3C): consumption = last − first.
        await Readings.InsertManyAsync(new[]
        {
            Reading(d, "energy", 10, now.AddSeconds(-6)),
            Reading(d, "energy", 12, now.AddSeconds(-3)),
            Reading(d, "energy", 15, now),
        });

        var delta = await Store.AggregateBatchAsync(
            new[] { new SeriesSpec(d, "energy") }, null, null, "hour", "delta", null, default);
        Assert.Equal(5, Assert.Single(delta.Single().Buckets).Value, 3); // 15 − 10

        var sum = await Store.AggregateBatchAsync(
            new[] { new SeriesSpec(d, "energy") }, null, null, "hour", "sum", null, default);
        Assert.Equal(37, Assert.Single(sum.Single().Buckets).Value, 3); // 10 + 12 + 15
    }

    [Fact]
    public async Task AggregateBatch_Delta_TreatsCounterResetAsRestart()
    {
        var d = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        // Counter reset mid-bucket (device reboot): last < first ⇒ delta reads as "restarted, then climbed
        // to last" (never a spurious negative).
        await Readings.InsertManyAsync(new[]
        {
            Reading(d, "energy", 40, now.AddSeconds(-6)),
            Reading(d, "energy", 42, now.AddSeconds(-3)),
            Reading(d, "energy", 3, now),
        });

        var delta = await Store.AggregateBatchAsync(
            new[] { new SeriesSpec(d, "energy") }, null, null, "hour", "delta", null, default);
        Assert.Equal(3, Assert.Single(delta.Single().Buckets).Value, 3); // reset → last (3)
    }

    [Fact]
    public async Task AggregateBatch_RejectsBadBucket_AndEmptySeriesIsEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.AggregateBatchAsync(
            new[] { new SeriesSpec("x", "y") }, null, null, "week", "avg", null, default));

        var empty = await Store.AggregateBatchAsync(
            Array.Empty<SeriesSpec>(), null, null, "hour", "avg", null, default);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task LatestByDevice_ReturnsMostRecentEventPerDevice()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await Events.InsertManyAsync(new[]
        {
            new DeviceEventLog
            {
                Timestamp = now.AddMinutes(-10), CapabilityId = "temperature",
                Meta = new EventMeta { DeviceId = a, ZoneId = "z" },
                TriggerSource = TriggerSources.Device, NewValue = 21.0,
            },
            new DeviceEventLog
            {
                Timestamp = now.AddMinutes(-1), CapabilityId = "on_off",
                Meta = new EventMeta { DeviceId = a, ZoneId = "z" },
                TriggerSource = TriggerSources.Rule, TriggerId = "rule-1", RuleId = "rule-1", NewValue = true,
            },
            new DeviceEventLog
            {
                Timestamp = now.AddMinutes(-5), CapabilityId = "power",
                Meta = new EventMeta { DeviceId = b, ZoneId = "z" },
                TriggerSource = TriggerSources.Ml, TriggerId = "block-9", NewValue = 42.0,
            },
        });

        var rows = await Store.LatestByDeviceAsync(new[] { a, b, Guid.NewGuid().ToString() }, null, null, default);

        Assert.Equal(2, rows.Count); // the unknown id has no events

        var latestA = rows.Single(r => r.DeviceId == a);
        Assert.Equal("on_off", latestA.CapabilityId); // newest wins over the -10min temperature row
        Assert.Equal(TriggerSources.Rule, latestA.TriggerSource);
        Assert.Equal("rule-1", latestA.RuleId);

        var latestB = rows.Single(r => r.DeviceId == b);
        Assert.Equal(TriggerSources.Ml, latestB.TriggerSource);
        Assert.Equal("block-9", latestB.TriggerId);
    }
}
