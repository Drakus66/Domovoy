// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// The presence platform layer (roadmap Epic 3D). Projects each tracked resident as a virtual <c>person</c>
/// device (capability <c>presence</c>) and publishes an aggregate <b>Occupancy</b> device
/// (<c>anyone_home</c> / <c>home_count</c>) — first-class devices that show on the dashboard and work in
/// rules/scenes/blocks like any sensor, exactly like the system sensors (2L) and the Home/Power devices
/// (1G/3C-LM). The aggregate is what the user binds into the <c>presence_mode</c> block (1G) to drive the
/// home mode by occupancy — this epic supplies the presence <b>source</b> that the removed PresenceMonitor
/// lacked, not the mode logic.
///
/// <para>It subscribes to <see cref="PresenceReportedV1"/> (the OwnTracks-compatible geofencing ingest in
/// the ApiGateway), resolves the resident, and applies the server-side geofence via <see cref="PresenceState"/>.
/// A periodic tick resolves grace-window departures (a resident who left stops reporting), re-announces the
/// roster and republishes state. Home coordinates come from the live site location (2K, via
/// <see cref="SunCalculator"/>); roster + geofence params are refreshed from the DbGateway each tick.</para>
/// </summary>
public sealed class PresenceService : BackgroundService
{
    private static readonly Guid OccupancyDeviceId = DeviceIdFactory.Derive(SystemSensorService.Source, "presence");
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private static readonly Capability[] PersonCapabilities = { WellKnownCapabilities.Presence() };
    private static readonly Capability[] OccupancyCapabilities =
    {
        WellKnownCapabilities.AnyoneHome(),
        WellKnownCapabilities.HomeCount(),
    };

    private readonly IMessageBus _bus;
    private readonly PresenceState _state;
    private readonly DbGatewayClient _db;
    private readonly SunCalculator _site;
    private readonly ILogger<PresenceService> _logger;

    // Last-announced display name per resident, so a rename re-announces (updates the read-model) without
    // re-announcing every tick. Accessed only from the single tick loop after the initial sync.
    private readonly Dictionary<string, string> _announced = new(StringComparer.Ordinal);

    public PresenceService(
        IMessageBus bus, PresenceState state, DbGatewayClient db, SunCalculator site, ILogger<PresenceService> logger)
    {
        _bus = bus;
        _state = state;
        _db = db;
        _site = site;
        _logger = logger;
    }

    private static Guid PersonDeviceId(string residentId) =>
        DeviceIdFactory.Derive(SystemSensorService.Source, $"person/{residentId}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Establish the roster + geofence + announce the devices before accepting reports, so a report never
        // lands on an unannounced device.
        await SyncAsync(stoppingToken);
        await AnnounceOccupancyAsync(stoppingToken);
        await PublishOccupancyAsync(stoppingToken);

        await _bus.SubscribeAsync<Envelope<PresenceReportedV1>>(
            "automation-presence-reports",
            BusTopology.EventsExchange,
            BusTopology.PresenceReportedKey,
            env => HandleReport(env, stoppingToken),
            stoppingToken);

        _logger.LogInformation("PresenceService started (tick {Seconds}s)", TickInterval.TotalSeconds);

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);

                // Flip any resident whose away-grace elapsed while they stopped reporting.
                var flipped = _state.EvaluatePending(DateTimeOffset.UtcNow);
                foreach (var id in flipped) await PublishPersonAsync(id, stoppingToken);

                // Steady-state republish (like the system sensors) so the read-model self-heals.
                foreach (var r in _state.Roster()) await PublishPersonAsync(r.Id, stoppingToken);
                await PublishOccupancyAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Presence tick failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    /// <summary>Refresh roster + geofence config from the DbGateway and announce any newly-seen / renamed residents.</summary>
    private async Task SyncAsync(CancellationToken ct)
    {
        var settings = await _db.GetPresenceSettingsAsync(ct);
        if (settings is not null)
            _state.Configure(settings.HomeRadiusMeters, settings.AwayGraceSeconds, _site.Latitude, _site.Longitude);
        else
            _state.Configure(150, 180, _site.Latitude, _site.Longitude);

        var residents = await _db.GetResidentsAsync(ct);
        if (residents is null) return; // gateway unreachable — keep the last-known roster (offline-first)

        var roster = residents.Select(r =>
            new PresenceState.ResidentEntry(r.Id, r.DisplayName, r.OwnTracksId, r.TrackingEnabled)).ToList();
        _state.SyncRoster(roster);

        foreach (var r in roster)
        {
            if (_announced.TryGetValue(r.Id, out var name) && name == r.DisplayName) continue;
            await AnnouncePersonAsync(r, ct);
            _announced[r.Id] = r.DisplayName;
        }
    }

    private async Task HandleReport(Envelope<PresenceReportedV1> envelope, CancellationToken ct)
    {
        var report = envelope.Data;
        if (report is null || report.ResidentKeys.Count == 0) return;

        var changed = _state.ApplyReport(
            report.ResidentKeys, report.Latitude, report.Longitude, report.ExplicitPresent,
            report.BatteryPercent, report.ReportedAt);

        // Republish the resident's device even on a battery-only report (so the value is fresh); republish the
        // aggregate only when a home/away actually flipped.
        var resident = ResolveResidentId(report.ResidentKeys);
        if (resident is not null) await PublishPersonAsync(resident, ct);
        if (changed is not null) await PublishOccupancyAsync(ct);
    }

    private string? ResolveResidentId(IReadOnlyList<string> keys)
    {
        foreach (var r in _state.Roster())
        {
            if (string.IsNullOrWhiteSpace(r.OwnTracksId)) continue;
            if (keys.Any(k => string.Equals(k, r.OwnTracksId, StringComparison.OrdinalIgnoreCase)))
                return r.Id;
        }
        return null;
    }

    private Task PublishPersonAsync(string residentId, CancellationToken ct)
    {
        var presence = _state.Get(residentId);
        var state = new Dictionary<string, object?> { [CapabilityIds.Presence] = presence?.Home ?? false };
        if (presence?.Battery is int b) state[CapabilityIds.Battery] = b;
        return PublishStateAsync(PersonDeviceId(residentId), $"person/{residentId}", state, ct);
    }

    private Task PublishOccupancyAsync(CancellationToken ct)
    {
        var (anyoneHome, homeCount) = _state.Aggregate();
        var state = new Dictionary<string, object?>
        {
            [CapabilityIds.AnyoneHome] = anyoneHome,
            [CapabilityIds.HomeCount] = homeCount,
        };
        return PublishStateAsync(OccupancyDeviceId, "presence", state, ct);
    }

    private Task AnnouncePersonAsync(PresenceState.ResidentEntry resident, CancellationToken ct) =>
        AnnounceAsync(PersonDeviceId(resident.Id), resident.DisplayName, $"person/{resident.Id}",
            "system/person", PersonCapabilities, ct);

    private Task AnnounceOccupancyAsync(CancellationToken ct) =>
        AnnounceAsync(OccupancyDeviceId, "Occupancy", "presence", "system/presence", OccupancyCapabilities, ct);

    private async Task PublishStateAsync(Guid deviceId, string tag, IReadOnlyDictionary<string, object?> state, CancellationToken ct)
    {
        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"{SystemSensorService.Source.ToLowerInvariant()}:{tag}",
            data: new DeviceStateReportV1(deviceId, state),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);
    }

    private async Task AnnounceAsync(
        Guid deviceId, string name, string hardwareId, string model, Capability[] capabilities, CancellationToken ct)
    {
        var descriptor = new DeviceDescriptor(
            Id: deviceId,
            Name: name,
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(SystemSensorService.Source, hardwareId),
            Capabilities: capabilities,
            Manufacturer: "Domovoy",
            Model: model);

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"{SystemSensorService.Source.ToLowerInvariant()}:{hardwareId}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope, ct);
        _logger.LogInformation("Announced presence device {Name} as {DeviceId}", name, deviceId);
    }
}
