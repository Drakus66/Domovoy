// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Publishes the platform tariff as a first-class virtual device (roadmap Epic 3C) — <c>system/tariff</c>
/// carrying the current price (per kWh) and the active tariff zone, exactly like the 2L system sensors
/// (<see cref="SystemSensorService"/>). It announces the device once — re-announcing only when the currency
/// or the set of zone names changes, since those shape the capabilities — and republishes the current
/// price/zone every minute from the live <see cref="TariffContext"/>. So the price flows into telemetry (a
/// price trend), the read-model and the rule engine like any other device, with <c>AdapterSource="System"</c>
/// marking it virtual and <c>Model="system/tariff"</c> giving it the <c>tariff</c> archetype.
/// </summary>
public sealed class TariffService : BackgroundService
{
    private static readonly Guid TariffDeviceId = DeviceIdFactory.Derive(SystemSensorService.Source, "tariff");
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IMessageBus _bus;
    private readonly TariffContext _tariff;
    private readonly ILogger<TariffService> _logger;
    private string? _announceSig;

    public TariffService(IMessageBus bus, TariffContext tariff, ILogger<TariffService> logger)
    {
        _bus = bus;
        _tariff = tariff;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TariffService started (tick {Seconds}s)", TickInterval.TotalSeconds);

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureAnnouncedAsync(stoppingToken);
                await PublishAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Tariff tick failed"); }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    // Re-announce only when the capability shape changes: the currency drives the price unit and the set of
    // zone names drives the tariff-zone enum. A signature of both dedupes the announce.
    private async Task EnsureAnnouncedAsync(CancellationToken ct)
    {
        var zones = _tariff.ZoneNames();
        var sig = _tariff.Currency + "|" + string.Join(",", zones);
        if (sig == _announceSig) return;

        var capabilities = new[]
        {
            WellKnownCapabilities.Price($"{_tariff.Currency}/kWh"),
            WellKnownCapabilities.TariffZone(zones),
        };
        var descriptor = new DeviceDescriptor(
            Id: TariffDeviceId,
            Name: "Tariff",
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(SystemSensorService.Source, "tariff"),
            Capabilities: capabilities,
            Manufacturer: "Domovoy",
            Model: "system/tariff");

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: "system:tariff",
            data: new DeviceDiscoveredV1(descriptor),
            subject: TariffDeviceId.ToString());
        await _bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope, ct);

        _announceSig = sig;
        _logger.LogInformation("Announced tariff device (currency {Currency}, zones {Zones})",
            _tariff.Currency, string.Join(",", zones));
    }

    private async Task PublishAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        var (zone, price) = _tariff.At(nowUtc);
        var state = new Dictionary<string, object?>
        {
            [CapabilityIds.Price] = Math.Round(price, 4),
            [CapabilityIds.TariffZone] = zone,
        };
        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: "system:tariff",
            data: new DeviceStateReportV1(TariffDeviceId, state),
            subject: TariffDeviceId.ToString());
        await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);
    }
}
