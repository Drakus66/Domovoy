// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;

using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Projects each global variable (roadmap Epic 3E, Hubitat "Hub Variables") as its own virtual capability
/// device — the "connector-device" requirement: rules read/write a variable exactly like any other device
/// capability (triggers, conditions, the <see cref="ActionType.Command"/> action, control-block port
/// bindings), with no separate variable-reference syntax anywhere else.
///
/// <para>Modeled on <see cref="Blocks.BlockRuntime"/>'s announce/sync pattern crossed with
/// <see cref="SystemSensorService"/>'s Home-device <b>durable-write</b> pattern: unlike the Power device (a
/// live signal that is never persisted), a variable's value must survive a restart, so a command round-trips
/// through the DbGateway before its state is echoed back — see <see cref="HandleCommand"/>.</para>
/// </summary>
public sealed class VariableRuntimeService : BackgroundService
{
    private const string Source = "Variable";
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly IMessageBus _bus;
    private readonly VariableStore _store;
    private readonly DeviceRegistry _registry;
    private readonly DbGatewayClient _db;
    private readonly ILogger<VariableRuntimeService> _logger;

    // variableId -> (deviceId, type) last announced, so a type change re-announces with the right capability
    // kind and a brand-new variable is picked up without waiting for a restart.
    private readonly ConcurrentDictionary<string, (Guid DeviceId, VariableType Type)> _announced = new();

    public VariableRuntimeService(
        IMessageBus bus, VariableStore store, DeviceRegistry registry, DbGatewayClient db,
        ILogger<VariableRuntimeService> logger)
    {
        _bus = bus;
        _store = store;
        _registry = registry;
        _db = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Capture commands aimed at a variable's virtual device.
        await _bus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            "automation-variable-commands",
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            env => HandleCommand(env, stoppingToken),
            stoppingToken);

        _logger.LogInformation("VariableRuntimeService started (tick {Seconds}s)", TickInterval.TotalSeconds);

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAndPublish(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VariableRuntimeService tick failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    // Announce any new/changed-type variable, then publish current state for every variable — every tick,
    // mirroring SystemSensorService's virtual sensors. No deletion path for v1: a variable removed from the
    // store just leaves its capability_devices row stale (acceptable v1 gap — there is no DeviceRemovedV1
    // bus message yet for other services to follow the pattern of).
    private async Task SyncAndPublish(CancellationToken ct)
    {
        foreach (var variable in _store.Variables)
        {
            var deviceId = DeviceIdFactory.Derive(Source, variable.Id);
            if (!_announced.TryGetValue(variable.Id, out var known) || known.Type != variable.Type)
            {
                await AnnounceDevice(variable, deviceId, ct);
                _announced[variable.Id] = (deviceId, variable.Type);
            }

            await PublishStateAsync(deviceId, variable.Id, variable.Value, ct);
        }
    }

    private async Task AnnounceDevice(GlobalVariable variable, Guid deviceId, CancellationToken ct)
    {
        var descriptor = new DeviceDescriptor(
            Id: deviceId,
            Name: variable.Name,
            ZoneId: Guid.Empty,
            Identity: new DeviceIdentity(Source, variable.Id),
            Capabilities: new[] { CapabilityFor(variable.Type) },
            Manufacturer: "Domovoy",
            Model: $"variable/{variable.Type}");

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"variable:{variable.Id}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope, ct);
        _logger.LogInformation("Announced variable '{Name}' ({Type}) as device {DeviceId}",
            variable.Name, variable.Type, deviceId);
    }

    private async Task PublishStateAsync(Guid deviceId, string variableId, object? value, CancellationToken ct)
    {
        var envelope = Envelope<DeviceStateReportV1>.Create(
            MessageTypes.DeviceState,
            source: $"variable:{variableId}",
            data: new DeviceStateReportV1(deviceId, new Dictionary<string, object?> { [CapabilityIds.VariableValue] = value }),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);
    }

    /// <summary>
    /// Device→durable-write bridge (mirrors <see cref="SystemSensorService.HandleHomeCommand"/>): a command
    /// setting <see cref="CapabilityIds.VariableValue"/> on a variable's virtual device mirrors into the
    /// blackboard immediately (so a same-tick read sees it), persists through the DbGateway (durable — a
    /// variable must survive a restart), then republishes state immediately rather than waiting for the
    /// next tick.
    /// </summary>
    private async Task HandleCommand(Envelope<DeviceCommandV1> envelope, CancellationToken ct)
    {
        var cmd = envelope.Data;
        if (cmd is null || !cmd.Set.TryGetValue(CapabilityIds.VariableValue, out var raw)) return;

        var variable = _store.Variables.FirstOrDefault(v => DeviceIdFactory.Derive(Source, v.Id) == cmd.DeviceId);
        if (variable is null) return;

        var value = ValueOps.Normalize(raw);

        // Mirror into the local blackboard immediately so a downstream reader composed on this variable
        // (a rule condition, a control-block input) sees the value this same tick — the bus round-trip
        // would otherwise add latency (same rationale as BlockRuntime.HandleCommand).
        _registry.SetValue(cmd.DeviceId, CapabilityIds.VariableValue, value);

        // Also update the cached store instance (GlobalVariable is mutable and this is the same reference the
        // 15s tick republishes): the DbGateway write is durable but the store only re-reads it once per
        // RefreshLoop cycle, so without this the next tick would republish the stale value and briefly revert
        // the change until the refresh lands.
        variable.Value = value;

        _logger.LogInformation("Variable '{Name}' command -> {Value} (by {Source})",
            variable.Name, value, string.IsNullOrEmpty(envelope.Source) ? "unknown" : envelope.Source);
        await _db.SetVariableValueAsync(variable.Id, value, ct);

        await PublishStateAsync(cmd.DeviceId, variable.Id, value, ct);
    }

    private static Capability CapabilityFor(VariableType type) => type switch
    {
        VariableType.Number => WellKnownCapabilities.Number(CapabilityIds.VariableValue, writable: true),
        VariableType.Boolean => WellKnownCapabilities.Boolean(CapabilityIds.VariableValue, true),
        _ => WellKnownCapabilities.Text(CapabilityIds.VariableValue, true),
    };
}
