using System.Diagnostics;

using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Home;
using Domovoy.Contracts.Messaging;
using Domovoy.DbGateway.Models;
using Domovoy.DbGateway.Services;

using MongoDB.Driver;

using Xunit;

namespace Domovoy.IntegrationTests;

/// <summary>
/// End-to-end verification of the capability data-path against real RabbitMQ + Mongo (roadmap Phase 1.5,
/// closes P0-4 for the adapter→bus→Mongo legs). The SignalR leg (ApiGateway relay) is not covered here.
/// </summary>
[Collection("infra")]
public sealed class EndToEndTests
{
    private readonly InfraFixture _fx;
    public EndToEndTests(InfraFixture fx) => _fx = fx;

    private IMongoCollection<CapabilityDeviceDocument> Devices => _fx.Db.GetCollection<CapabilityDeviceDocument>("capability_devices");
    private IMongoCollection<DeviceEventLog> Events => _fx.Db.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection);
    private IMongoCollection<SensorReading> Readings => _fx.Db.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection);

    private static async Task<bool> WaitFor(Func<Task<bool>> condition, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (await condition()) return true;
            await Task.Delay(250);
        }
        return false;
    }

    [Fact]
    [Trait("Category", "OfflineSmoke")]
    public async Task StatePath_PersistsReadModel_EventLog_AndTelemetry()
    {
        var id = Guid.NewGuid();
        var descriptor = new DeviceDescriptor(
            id, "IT Sensor", Guid.Empty,
            new DeviceIdentity("IntegrationTest", id.ToString()),
            new[] { WellKnownCapabilities.Temperature(), WellKnownCapabilities.OnOff() });

        await _fx.Bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey,
            Envelope<DeviceDiscoveredV1>.Create(MessageTypes.DeviceDiscovered, "it", new DeviceDiscoveredV1(descriptor), id.ToString()));

        Assert.True(await WaitFor(async () => await Devices.Find(x => x.Id == id.ToString()).AnyAsync()),
            "device read-model not persisted from discovery");

        await _fx.Bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey,
            Envelope<DeviceStateReportV1>.Create(MessageTypes.DeviceState, "it",
                new DeviceStateReportV1(id, new Dictionary<string, object?> { ["temperature"] = 21.5, ["on_off"] = true }), id.ToString()));

        Assert.True(await WaitFor(async () =>
        {
            var d = await Devices.Find(x => x.Id == id.ToString()).FirstOrDefaultAsync();
            return d is not null && d.State.ContainsKey("temperature");
        }), "state not merged into read-model");

        Assert.True(await WaitFor(async () => await Events.Find(
                Builders<DeviceEventLog>.Filter.Eq(x => x.Meta.DeviceId, id.ToString())
                & Builders<DeviceEventLog>.Filter.Eq(x => x.CapabilityId, "temperature")).AnyAsync()),
            "event-log delta not written to device_events");

        Assert.True(await WaitFor(async () => await Readings.Find(
                Builders<SensorReading>.Filter.Eq(x => x.Meta.DeviceId, id.ToString())
                & Builders<SensorReading>.Filter.Eq(x => x.Meta.CapabilityId, "temperature")).AnyAsync()),
            "telemetry sample not written to sensor_readings");

        var doc = await Devices.Find(x => x.Id == id.ToString()).FirstAsync();
        Assert.Equal(21.5, Convert.ToDouble(doc.State["temperature"]), 3);
        Assert.True(Convert.ToBoolean(doc.State["on_off"]));
        // Semantic typing (Epic 2D): writable on_off (+ temperature) classifies as a switch.
        Assert.Equal(DeviceArchetypes.Switch, doc.AutoArchetype);

        var reading = await Readings.Find(x => x.Meta.DeviceId == id.ToString() && x.Meta.CapabilityId == "temperature").FirstAsync();
        Assert.Equal(21.5, reading.Value, 3);
    }

    [Fact]
    public async Task HomeMode_Change_RecordedInEventLog()
    {
        await _fx.Bus.PublishAsync(BusTopology.EventsExchange, BusTopology.HomeModeChangedKey,
            Envelope<HomeModeChangedV1>.Create(MessageTypes.HomeModeChanged, "it",
                new HomeModeChangedV1(WellKnownModes.Away, WellKnownModes.Home, ModeChangeSources.User, DateTimeOffset.UtcNow),
                WellKnownModes.Away));

        Assert.True(await WaitFor(async () => await Events.Find(
                Builders<DeviceEventLog>.Filter.Eq(x => x.Meta.Kind, EventKinds.ModeChange)
                & Builders<DeviceEventLog>.Filter.Eq(x => x.CapabilityId, ContextCapabilities.HomeMode)
                & Builders<DeviceEventLog>.Filter.Eq(x => x.NewValue, WellKnownModes.Away)).AnyAsync()),
            "home-mode change not recorded in event-log");
    }

    [Fact]
    public async Task NewModels_RoundTripThroughMongo()
    {
        var blocks = _fx.Db.GetCollection<ControlBlock>("control_blocks");
        var block = new ControlBlock
        {
            Id = Guid.NewGuid().ToString(), Name = "IT block", TypeId = "thermostat",
            DeviceId = Guid.NewGuid().ToString(),
            Params = new() { ["setpoint"] = 21.5, ["hysteresis"] = 0.5 },
            Inputs = new() { ["temperature"] = new PortBinding { DeviceId = "sensor", CapabilityId = "temperature" } },
            Outputs = new() { ["on_off"] = new PortBinding { DeviceId = "boiler", CapabilityId = "on_off" } },
        };
        await blocks.InsertOneAsync(block);
        var back = await blocks.Find(x => x.Id == block.Id).FirstAsync();
        Assert.Equal("thermostat", back.TypeId);
        Assert.Equal(21.5, back.Params["setpoint"], 3);
        Assert.Equal("boiler", back.Outputs["on_off"].DeviceId);

        var home = _fx.Db.GetCollection<HomeState>("home_state");
        await home.ReplaceOneAsync(x => x.Id == HomeState.SingletonId,
            new HomeState { Mode = WellKnownModes.Night, Source = ModeChangeSources.User },
            new ReplaceOptions { IsUpsert = true });
        var hs = await home.Find(x => x.Id == HomeState.SingletonId).FirstAsync();
        Assert.Equal(WellKnownModes.Night, hs.Mode);
    }
}
