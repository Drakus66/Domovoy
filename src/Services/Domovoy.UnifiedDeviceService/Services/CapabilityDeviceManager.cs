// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.UnifiedDeviceService.Services;

using System.Collections.Concurrent;

using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Events;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Capability-contract consumer (roadmap Step 3). Subscribes to <see cref="DeviceDiscoveredV1"/> and
/// <see cref="DeviceStateReportV1"/> published by adapters on the canonical bus topology, maintains an
/// in-memory capability device registry, and re-emits normalized state as the existing
/// <see cref="DeviceStateUpdatedEvent"/> so the SignalR relay and persistence interceptor keep working.
/// <para>
/// Runs in parallel with the legacy <see cref="UnifiedDeviceManager"/> during migration. Persistence to
/// the DB and UI cutover are Step 4; legacy-path removal is Step 5. Device ids are deterministic
/// (see <see cref="DeviceIdFactory"/>), so duplicates across restarts cannot occur even without a
/// persisted registry — the in-memory registry simply repopulates when adapters re-announce.
/// </para>
/// </summary>
public sealed class CapabilityDeviceManager : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly ILogger<CapabilityDeviceManager> _logger;
    private readonly ConcurrentDictionary<Guid, CapabilityDeviceRecord> _devices = new();

    public CapabilityDeviceManager(IMessageBus bus, ILogger<CapabilityDeviceManager> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.SubscribeAsync<Envelope<DeviceDiscoveredV1>>(
            "unifieddevice-capability-discovery",
            BusTopology.DiscoveryExchange,
            BusTopology.DeviceDiscoveredKey,
            HandleDiscovered,
            stoppingToken);

        await _bus.SubscribeAsync<Envelope<DeviceStateReportV1>>(
            "unifieddevice-capability-state",
            BusTopology.StateExchange,
            BusTopology.DeviceStateUpdatedKey,
            HandleStateReport,
            stoppingToken);

        _logger.LogInformation("CapabilityDeviceManager subscribed to capability discovery + state");
    }

    private Task HandleDiscovered(Envelope<DeviceDiscoveredV1> envelope)
    {
        var descriptor = envelope.Data?.Device;
        if (descriptor is null) return Task.CompletedTask;

        _devices.AddOrUpdate(
            descriptor.Id,
            _ => new CapabilityDeviceRecord(descriptor),
            (_, existing) => { existing.Descriptor = descriptor; return existing; });

        _logger.LogInformation(
            "Capability device registered: '{Name}' ({DeviceId}) caps=[{Caps}]",
            descriptor.Name, descriptor.Id, string.Join(", ", descriptor.Capabilities.Select(c => c.Id)));

        return Task.CompletedTask;
    }

    private async Task HandleStateReport(Envelope<DeviceStateReportV1> envelope)
    {
        var report = envelope.Data;
        if (report is null) return;

        // Merge into the in-memory record (the device may not have been discovered yet).
        if (_devices.TryGetValue(report.DeviceId, out var record))
        {
            foreach (var kv in report.State)
                record.State[kv.Key] = kv.Value;
        }

        // Re-emit as the existing downstream event so EventRelayService (SignalR) and
        // EventInterceptor (persistence) — bound to device.events / device.state.updated — keep working.
        var state = report.State
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);

        var stateEvent = new DeviceStateUpdatedEvent
        {
            DeviceId = report.DeviceId.ToString(),
            State = state,
            Timestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            Success = true,
            Source = nameof(CapabilityDeviceManager)
        };

        await _bus.PublishAsync(
            MessageBusConfiguration.DeviceEventsExchange,
            MessageBusConfiguration.DeviceStateUpdatedRoutingKey,
            stateEvent);

        _logger.LogDebug("Re-emitted normalized state for {DeviceId} ({Count} capabilities)",
            report.DeviceId, state.Count);
    }

    /// <summary>In-memory record of a capability device and its latest normalized state.</summary>
    private sealed class CapabilityDeviceRecord(DeviceDescriptor descriptor)
    {
        public DeviceDescriptor Descriptor { get; set; } = descriptor;
        public ConcurrentDictionary<string, object?> State { get; } = new();
    }
}
