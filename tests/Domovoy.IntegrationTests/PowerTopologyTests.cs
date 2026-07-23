// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Devices;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using Microsoft.Extensions.Logging.Abstractions;

using MongoDB.Bson;
using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// The electrical topology against real Mongo (roadmap Epic 3C-D): consumption projected onto circuits and
/// phases, the balance check that surfaces load a line's meter sees but no device explains, and the one-shot
/// migration of the legacy <c>energyRole</c> field into the per-device energy profile. Unique ids isolate the
/// data from the shared fixture.
/// </summary>
[Collection("infra")]
[Trait("Category", "Infra")]
public sealed class PowerTopologyTests
{
    private readonly InfraFixture _fx;
    public PowerTopologyTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<SensorReading> Readings =>
        _fx.Db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection);
    private IMongoCollection<CapabilityDeviceDocument> Devices =>
        _fx.Db.GetCollection<CapabilityDeviceDocument>(CapabilityDeviceEndpoints.Collection);
    private IMongoCollection<PowerNode> Nodes =>
        _fx.Db.GetCollection<PowerNode>(PowerTopologyEndpoints.Collection);

    private static CapabilityDeviceDocument Device(
        string id, string name, string? circuitId, double? powerW = null, string? role = null) => new()
    {
        Id = id, Name = name, ZoneId = "z", AdapterSource = "Zigbee2Mqtt",
        AutoArchetype = DeviceArchetypes.EnergyMeter,
        Capabilities = new()
        {
            new CapabilityDocument { Id = "energy", Kind = "Number", Unit = "kWh" },
            new CapabilityDocument { Id = "power", Kind = "Number", Unit = "W" },
        },
        State = powerW is { } w ? new Dictionary<string, object> { ["power"] = w } : new(),
        EnergyProfile = new EnergyProfile { Track = true, CircuitId = circuitId, Role = role },
    };

    private static SensorReading Energy(string id, double value, DateTime ts) => new()
    {
        Timestamp = ts,
        Meta = new TelemetryMeta { DeviceId = id, ZoneId = "z", CapabilityId = "energy" },
        Value = value,
    };

    [Fact]
    public async Task Breakdown_AttributesConsumptionToCircuitsAndPhases_AndReportsUnaccounted()
    {
        var circuitId = Guid.NewGuid().ToString();
        var meterId = Guid.NewGuid().ToString();
        var lampId = Guid.NewGuid().ToString();
        var strayId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await Nodes.InsertOneAsync(new PowerNode
        {
            Id = circuitId, Name = "Kitchen line", Kind = PowerNodeKinds.Circuit,
            Phase = PowerPhases.L2, BreakerAmps = 16, MeterDeviceId = meterId,
        });

        await Devices.InsertManyAsync(new[]
        {
            // On the circuit: 3 kWh of load, 120 W right now.
            Device(lampId, "Kitchen lamp", circuitId, powerW: 120),
            // The line's own meter saw 5 kWh — it is a meter, not a load on the line.
            Device(meterId, "Kitchen meter", circuitId: null, powerW: 400, role: EnergyEndpoints.MainsRole),
            // Tracked but never attached to a circuit → reported as unmapped, not silently folded into a line.
            Device(strayId, "Stray heater", circuitId: null, powerW: 900),
        });

        await Readings.InsertManyAsync(new[]
        {
            Energy(lampId, 10, now.AddSeconds(-6)), Energy(lampId, 13, now),      // 3 kWh
            Energy(meterId, 100, now.AddSeconds(-6)), Energy(meterId, 105, now),  // 5 kWh measured on the line
            Energy(strayId, 1, now.AddSeconds(-6)), Energy(strayId, 3, now),      // 2 kWh, unmapped
        });

        var res = await EnergyEndpoints.BreakdownAsync(_fx.Db, from: null, to: null);

        var circuit = res.Nodes.Single(n => n.NodeId == circuitId);
        Assert.Equal(3, circuit.Kwh, 3);              // only what the mapped devices explain
        Assert.Equal(120, circuit.PowerW, 1);
        Assert.Equal(5, circuit.MeterKwh!.Value, 3);  // what the line's meter actually measured
        Assert.Equal(2, circuit.UnaccountedKwh!.Value, 3); // 5 − 3: load nothing on the line explains
        Assert.Equal(16 * 230, circuit.LimitWatts!.Value, 1); // breaker rating → watts
        Assert.Equal(PowerPhases.L2, circuit.Phase);

        // Phase totals are shared with whatever else the fixture put on L2, so assert containment.
        var phase = res.Phases.Single(p => p.Phase == PowerPhases.L2);
        Assert.True(phase.Kwh >= 3, $"expected ≥3 kWh on L2, got {phase.Kwh}");
        Assert.True(phase.PowerW >= 120, $"expected ≥120 W on L2, got {phase.PowerW}");

        // The stray device counts toward the household totals but cannot be attributed to a line.
        Assert.True(res.UnmappedKwh >= 2, $"expected the stray 2 kWh in unmapped, got {res.UnmappedKwh}");
        Assert.True(res.UnmappedPowerW >= 900, $"expected the stray 900 W in unmapped, got {res.UnmappedPowerW}");
    }

    [Fact]
    public async Task Breakdown_ThreePhaseCircuit_SplitsEvenlyAcrossPhases()
    {
        var circuitId = Guid.NewGuid().ToString();
        var stoveId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        await Nodes.InsertOneAsync(new PowerNode
        {
            Id = circuitId, Name = "Stove line", Kind = PowerNodeKinds.Circuit, Phase = PowerPhases.Three,
        });
        await Devices.InsertOneAsync(Device(stoveId, "Stove", circuitId, powerW: 3000));
        await Readings.InsertManyAsync(new[]
        {
            Energy(stoveId, 50, now.AddSeconds(-6)), Energy(stoveId, 56, now), // 6 kWh over three phases
        });

        var res = await EnergyEndpoints.BreakdownAsync(_fx.Db, from: null, to: null);

        // Each phase carries a third of a three-phase load (v1 has no per-phase metering of one appliance).
        foreach (var phase in new[] { PowerPhases.L1, PowerPhases.L2, PowerPhases.L3 })
        {
            var row = res.Phases.Single(p => p.Phase == phase);
            Assert.True(row.Kwh >= 2, $"{phase}: expected ≥2 kWh, got {row.Kwh}");
            Assert.True(row.PowerW >= 1000, $"{phase}: expected ≥1000 W, got {row.PowerW}");
        }
    }

    [Fact]
    public async Task EnergyRoleMigration_FoldsLegacyFieldIntoTheProfile()
    {
        var mainsId = Guid.NewGuid().ToString();
        var excludedId = Guid.NewGuid().ToString();

        // Insert as raw BSON: the legacy documents predate the profile, and the field is gone from the API.
        var raw = _fx.Db.GetCollection<BsonDocument>(CapabilityDeviceEndpoints.Collection);
        await raw.InsertManyAsync(new[]
        {
            new BsonDocument { ["_id"] = mainsId, ["Name"] = "Old mains", ["EnergyRole"] = "mains" },
            new BsonDocument { ["_id"] = excludedId, ["Name"] = "Old rig", ["EnergyRole"] = "excluded" },
        });

        await EnergyProfileMigration.RunAsync(_fx.Db, NullLogger.Instance);

        var mains = await Devices.Find(x => x.Id == mainsId).FirstAsync();
        Assert.Equal(EnergyEndpoints.MainsRole, mains.EnergyProfile?.Role);
        Assert.Null(mains.EnergyRole);

        var excluded = await Devices.Find(x => x.Id == excludedId).FirstAsync();
        Assert.False(excluded.EnergyProfile?.Track);
        Assert.Null(excluded.EnergyRole);

        // Idempotent: a second pass has nothing left to migrate and changes nothing.
        await EnergyProfileMigration.RunAsync(_fx.Db, NullLogger.Instance);
        Assert.Equal(EnergyEndpoints.MainsRole,
            (await Devices.Find(x => x.Id == mainsId).FirstAsync()).EnergyProfile?.Role);
    }

    [Fact]
    public void SyntheticCapabilities_FillOnlyWhatTheDeviceCannotMeasure()
    {
        var onOff = new CapabilityDocument { Id = "on_off", Kind = "Boolean", Writable = true };
        var power = new CapabilityDocument { Id = "power", Kind = "Number", Unit = "W" };
        var energy = new CapabilityDocument { Id = "energy", Kind = "Number", Unit = "kWh" };

        // Untracked: nothing is added.
        Assert.Equal(new[] { "on_off" },
            SyntheticCapabilities.Apply(new[] { onOff }, null).Select(c => c.Id));

        // No measurement at all → both series are synthesized.
        var estimated = SyntheticCapabilities.Apply(new[] { onOff }, new EnergyProfile { Track = true });
        Assert.Equal(new[] { "on_off", "power", "energy" }, estimated.Select(c => c.Id));
        Assert.All(estimated.Where(c => c.Id != "on_off"), c => Assert.True(c.Synthetic));

        // A wattmeter → only the integrated counter is added, the measured power is left alone.
        var integrated = SyntheticCapabilities.Apply(new[] { onOff, power }, new EnergyProfile { Track = true });
        Assert.Equal(new[] { "on_off", "power", "energy" }, integrated.Select(c => c.Id));
        Assert.False(integrated.Single(c => c.Id == "power").Synthetic);

        // A real energy counter → nothing to synthesize.
        Assert.Equal(new[] { "on_off", "energy" },
            SyntheticCapabilities.Apply(new[] { onOff, energy }, new EnergyProfile { Track = true }).Select(c => c.Id));

        // Idempotent: re-applying never duplicates the synthetic entries.
        var twice = SyntheticCapabilities.Apply(estimated, new EnergyProfile { Track = true });
        Assert.Equal(estimated.Select(c => c.Id), twice.Select(c => c.Id));
    }
}
