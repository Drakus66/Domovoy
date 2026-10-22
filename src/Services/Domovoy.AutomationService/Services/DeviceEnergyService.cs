// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Maintains the consumption series of devices that cannot report it themselves (roadmap Epic 3C-D). This is
/// the runtime behind the per-device energy profile and replaces the old Powercalc/integrator control blocks:
/// instead of a separate virtual device per estimate, the derived <c>power</c>/<c>energy</c> is published
/// <b>on behalf of the device itself</b>, so the prices, the dashboards, the trends and the rules all see one
/// device with one consumption series.
/// <list type="bullet">
///   <item>device reports <c>energy</c> — nothing to do, the counter is the truth;</item>
///   <item>device reports only <c>power</c> — integrate the measured watts into kWh;</item>
///   <item>device measures nothing — estimate watts from its state (<see cref="EnergyModel"/>), then integrate.</item>
/// </list>
/// <para>Ticks slowly and only republishes on a meaningful change, so an estimated device produces trend-grade
/// telemetry rather than a flood. A device that goes offline pauses: unknown state is not zero consumption,
/// and the gap guard in <see cref="EnergyModel.Accumulate"/> keeps the counter honest when it returns.</para>
/// </summary>
public sealed class DeviceEnergyService : BackgroundService
{
    /// <summary>How often the estimate is recomputed and integrated.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>Republish even when nothing changed, so a steady load still leaves a telemetry trail.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>Power change (W) below which a republish is not worth an event.</summary>
    private const double PowerEpsilonW = 0.5;

    private readonly IMessageBus _bus;
    private readonly DeviceRegistry _registry;
    private readonly ILogger<DeviceEnergyService> _logger;

    private readonly object _gate = new();
    private IReadOnlyList<TrackedDevice> _tracked = Array.Empty<TrackedDevice>();
    private readonly Dictionary<Guid, Accumulator> _accumulators = new();

    public DeviceEnergyService(IMessageBus bus, DeviceRegistry registry, ILogger<DeviceEnergyService> logger)
    {
        _bus = bus;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>A device whose consumption this service derives, resolved from the read-model.</summary>
    private sealed record TrackedDevice(
        Guid Id, string Name, bool IsOnline, bool HasPowerMeter,
        double MaxPowerW, double MinPowerW, double StandbyPowerW, string? ScaleCapabilityId);

    /// <summary>Per-device integration state: the cumulative counter plus when it last advanced/published.</summary>
    private sealed class Accumulator
    {
        public double Kwh;
        public DateTimeOffset? LastTick;
        public double LastPublishedPowerW = double.NaN;
        public DateTimeOffset LastPublishedAt = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Refresh the tracked-device set from the read-model — called by <see cref="RefreshLoop"/> with the device
    /// list it already fetched (no second round-trip), exactly like <see cref="LoadManager.Sync"/>. A device's
    /// counter is seeded from its last known <c>energy</c> value, so a service restart continues the series
    /// instead of starting over at zero.
    /// </summary>
    public void Sync(IReadOnlyList<DbGatewayClient.DeviceSnapshot> devices)
    {
        var tracked = new List<TrackedDevice>();

        foreach (var d in devices)
        {
            if (!Guid.TryParse(d.Id, out var id)) continue;
            if (!d.TracksEnergy) continue;

            // A device with its own energy counter needs nothing from us.
            var metered = d.Capabilities.Any(c => c.Id == CapabilityIds.Energy && !c.Synthetic);
            if (metered) continue;

            var profile = d.EnergyProfile;
            tracked.Add(new TrackedDevice(
                id,
                d.Name,
                d.IsOnline,
                HasPowerMeter: d.Capabilities.Any(c => c.Id == CapabilityIds.Power && !c.Synthetic),
                MaxPowerW: profile?.MaxPowerW ?? 0,
                MinPowerW: profile?.MinPowerW ?? 0,
                StandbyPowerW: profile?.StandbyPowerW ?? 0,
                ScaleCapabilityId: EnergyScale.Resolve(profile?.ScaleCapabilityId, d.Capabilities)));

            SeedAccumulator(id, d);
        }

        lock (_gate) _tracked = tracked;
    }

    /// <summary>Seed a device's cumulative counter from the read-model once (later ticks own it).</summary>
    private void SeedAccumulator(Guid id, DbGatewayClient.DeviceSnapshot device)
    {
        lock (_gate)
        {
            if (_accumulators.ContainsKey(id)) return;

            var seed = 0d;
            if (device.State.TryGetValue(CapabilityIds.Energy, out var raw)
                && raw.ValueKind == System.Text.Json.JsonValueKind.Number
                && raw.TryGetDouble(out var kwh) && kwh > 0)
                seed = kwh;

            _accumulators[id] = new Accumulator { Kwh = seed };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceEnergyService started (tick {Seconds}s)", TickInterval.TotalSeconds);

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Energy estimation tick failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break;
        }
    }

    private async Task TickAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<TrackedDevice> tracked;
        lock (_gate) tracked = _tracked;

        foreach (var device in tracked)
        {
            if (ct.IsCancellationRequested) return;

            Accumulator acc;
            lock (_gate)
            {
                if (!_accumulators.TryGetValue(device.Id, out var found)) continue;
                acc = found;
            }

            // Offline ⇒ pause: drop the clock so the downtime is a gap, not consumption at the last known draw.
            if (!device.IsOnline)
            {
                acc.LastTick = null;
                continue;
            }

            var powerW = CurrentPowerW(device);
            if (powerW is not { } watts) continue; // a measured device that has never reported yet

            if (acc.LastTick is { } last)
                acc.Kwh = EnergyModel.Accumulate(acc.Kwh, watts, now - last);
            acc.LastTick = now;

            if (!ShouldPublish(acc, watts, now)) continue;

            var state = new Dictionary<string, object?> { [CapabilityIds.Energy] = Math.Round(acc.Kwh, 6) };
            // Only an estimated draw is published — a device with a wattmeter reports its own.
            if (!device.HasPowerMeter) state[CapabilityIds.Power] = Math.Round(watts, 2);

            await PublishAsync(device.Id, state, ct);
            acc.LastPublishedPowerW = watts;
            acc.LastPublishedAt = now;
        }
    }

    /// <summary>The device's current draw: measured when it has a wattmeter, else estimated from its state.</summary>
    private double? CurrentPowerW(TrackedDevice device)
    {
        if (device.HasPowerMeter)
            return _registry.GetValue(device.Id, CapabilityIds.Power) is double measured ? Math.Max(0, measured) : null;

        var isOn = _registry.GetValue(device.Id, CapabilityIds.OnOff) as bool?;
        var scale = device.ScaleCapabilityId is { } id ? _registry.GetValue(device.Id, id) as double? : null;

        // With no on/off capability the regulator itself says whether the load runs (a dimmer at 0 %).
        return EnergyModel.EstimatePowerW(
            device.MaxPowerW, device.MinPowerW, device.StandbyPowerW, isOn ?? scale is > 0, scale);
    }

    private static bool ShouldPublish(Accumulator acc, double watts, DateTimeOffset now) =>
        double.IsNaN(acc.LastPublishedPowerW)
        || Math.Abs(watts - acc.LastPublishedPowerW) >= PowerEpsilonW
        || now - acc.LastPublishedAt >= HeartbeatInterval;

    /// <summary>
    /// Publish the derived series as the device's own state. The <c>energy:</c> actor-string tells the
    /// DbGateway this is a platform estimate: it updates the read-model and telemetry, but does not touch
    /// reachability and does not enter the activity journal (see EventInterceptor).
    /// </summary>
    private async Task PublishAsync(Guid deviceId, IReadOnlyDictionary<string, object?> state, CancellationToken ct)
    {
        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"energy:{deviceId}",
            data: new DeviceStateReportV1(deviceId, state),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);
    }
}
