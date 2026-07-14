// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;
using System.Globalization;

using Domovoy.AutomationService.Services;
using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Devices;
using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

namespace Domovoy.AutomationService.Blocks;

/// <summary>
/// Runtime health of a single control block, reported to the authoring UI (roadmap Epic 1H).
/// <paramref name="LastTickAt"/> is null until the block first ticks; <paramref name="LastError"/> holds
/// the message of the most recent failing tick (cleared on the next success).
/// </summary>
public sealed record BlockStatus(
    string BlockId,
    bool Enabled,
    DateTime? LastTickAt,
    long TickCount,
    int LastEmittedCount,
    string? LastError,
    DateTime? LastErrorAt);

/// <summary>
/// Hosts and ticks control-block instances (roadmap Epic 1H) — the stateful middle layer. On its own
/// fast cadence (separate from the 1-minute rule scheduler) it: syncs running instances with the configs
/// in <see cref="BlockStore"/>; reads each block's bound inputs from the shared <see cref="DeviceRegistry"/>
/// blackboard; ticks it; and publishes the emitted outputs as a <b>virtual capability device</b>'s state
/// (<see cref="DeviceStateReportV1"/>, source <c>block:{id}</c>). Each block is announced once via
/// <see cref="DeviceDiscoveredV1"/> so it appears in <c>/devices</c> with history/zones. Composition is
/// free: a block whose input is bound to another block's virtual device reads it from the registry, which
/// is kept live by the <see cref="AutomationEngine"/> subscription. Writable outputs (setpoints) are
/// retargeted by commanding the virtual device (<see cref="DeviceCommandV1"/>).
/// </summary>
public sealed class BlockRuntime : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly DeviceRegistry _registry;
    private readonly BlockCatalog _catalog;
    private readonly BlockStore _store;
    private readonly BlockStateStore _stateStore;
    private readonly ZoneCache _zones;
    private readonly ILogger<BlockRuntime> _logger;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);
    // Persist state every this-many ticks (2Q): a snapshot roughly every 2 min, deduped on unchanged JSON.
    private const int PersistEveryTicks = 8;
    private readonly ConcurrentDictionary<string, RunningBlock> _running = new();
    private long _tickCounter;

    public BlockRuntime(
        IMessageBus bus, DeviceRegistry registry, BlockCatalog catalog, BlockStore store,
        BlockStateStore stateStore, ZoneCache zones, ILogger<BlockRuntime> logger)
    {
        _bus = bus;
        _registry = registry;
        _catalog = catalog;
        _store = store;
        _stateStore = stateStore;
        _zones = zones;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Capture setpoint (and other writable-output) commands aimed at a block's virtual device.
        await _bus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            "automation-block-commands",
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            env => HandleCommand(env),
            stoppingToken);

        // 2Q: load the last persisted state so latches/counters/timers resume where they left off.
        await _stateStore.LoadAsync(stoppingToken);

        _logger.LogInformation("BlockRuntime started (tick {Seconds}s)", TickInterval.TotalSeconds);

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _zones.RefreshAsync(stoppingToken); // 2I: keep zone-kind lookup fresh for model-scope chains
                await SyncInstances(stoppingToken);
                await TickAll(stoppingToken);
                if (++_tickCounter % PersistEveryTicks == 0) await PersistState(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BlockRuntime tick failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }

        // Final snapshot on graceful shutdown so nothing since the last periodic save is lost.
        await PersistState(CancellationToken.None);
    }

    // 2Q: snapshot each running block's state bag (BlockStateStore dedupes unchanged JSON).
    private async Task PersistState(CancellationToken ct)
    {
        foreach (var rb in _running.Values)
            await _stateStore.SaveAsync(rb.Config.Id, rb.State, ct);
    }

    // Reconcile running instances with the latest configs: add new, drop removed, rebuild on config change.
    private async Task SyncInstances(CancellationToken ct)
    {
        var configs = _store.Blocks.ToDictionary(b => b.Id, StringComparer.Ordinal);

        foreach (var goneId in _running.Keys.Where(id => !configs.ContainsKey(id)).ToList())
        {
            _running.TryRemove(goneId, out _);
            await _stateStore.DeleteAsync(goneId, ct); // 2Q: drop persisted state for a removed block
        }

        foreach (var config in configs.Values)
        {
            if (_running.TryGetValue(config.Id, out var existing) && existing.ConfigStamp == config.UpdatedAt)
                continue; // unchanged

            var type = _catalog.Get(config.TypeId);
            if (type is null)
            {
                _logger.LogWarning("Block {Id} references unknown type {Type}", config.Id, config.TypeId);
                continue;
            }

            var deviceId = ResolveDeviceId(config);
            var running = new RunningBlock(config, type.Create(), deviceId, config.UpdatedAt);
            // 2Q: re-seed persisted state so a restart (or config edit) resumes latches/counters/timers.
            var saved = _stateStore.Take(config.Id);
            if (saved is not null)
                foreach (var kv in saved) running.State[kv.Key] = kv.Value;
            _running[config.Id] = running;

            await AnnounceDevice(config, type, deviceId, ct);
        }
    }

    private async Task TickAll(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        foreach (var rb in _running.Values)
        {
            if (!rb.Config.Enabled) continue;

            var emitted = new Dictionary<string, object?>();
            try
            {
                rb.Block.Tick(new BlockContext(rb, _registry, _zones, now, emitted, _logger));
                // Successful tick: stamp health so the UI can tell the block is alive and clear a stale error.
                rb.LastTickAt = DateTime.UtcNow;
                rb.TickCount++;
                rb.LastEmittedCount = emitted.Count;
                rb.LastError = null;
                rb.LastErrorAt = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Block {Id} ({Type}) tick threw", rb.Config.Id, rb.Config.TypeId);
                rb.LastError = ex.Message;
                rb.LastErrorAt = DateTime.UtcNow;
                // A block is an active entity: surface a failing tick in the Activity Center so it is not
                // silently stuck (Epic 1H run record). Deduped so a persistent fault logs once, not every tick.
                await PublishBlockRun(rb, ok: false, EmptyEmitted, ex.Message, ct);
                continue;
            }

            // Run record (Epic 1H): a block ticks continuously, so publish only when its emitted decision
            // changes — this is what makes the block layer visible in the journal, including a Shadow-stage
            // governor that emits a proposal but drives nothing. Deduped on the emitted signature inside.
            await PublishBlockRun(rb, ok: true, emitted, detail: null, ct);

            if (emitted.Count == 0) continue;

            // Mirror into the local blackboard immediately so a downstream block composed on this one
            // sees the value this same round (the bus round-trip would otherwise add a tick of latency).
            foreach (var kv in emitted) _registry.SetValue(rb.DeviceId, kv.Key, kv.Value);

            var envelope = Envelope<DeviceStateReportV1>.Create(
                MessageTypes.DeviceState,
                source: $"block:{rb.Config.Id}",
                data: new DeviceStateReportV1(rb.DeviceId, emitted),
                subject: rb.DeviceId.ToString());
            await _bus.PublishAsync(BusTopology.StateExchange, BusTopology.DeviceStateUpdatedKey, envelope, ct);

            // Actuation (roadmap Epic 1D): a bound output drives a real device — command it on change.
            await ActuateOutputs(rb, emitted, ct);
        }
    }

    // Publish a DeviceCommandV1 to the bound target whenever a wired output's value changes (Epic 1D).
    // Dedup on value so we don't re-command the actuator every tick.
    private async Task ActuateOutputs(RunningBlock rb, Dictionary<string, object?> emitted, CancellationToken ct)
    {
        foreach (var (capId, binding) in rb.Config.Outputs)
        {
            if (!emitted.TryGetValue(capId, out var value)) continue;
            if (!Guid.TryParse(binding.DeviceId, out var target) || string.IsNullOrEmpty(binding.CapabilityId)) continue;

            if (rb.LastCommanded.TryGetValue(capId, out var prev) && ValueOps.ValuesEqual(prev, value)) continue;
            rb.LastCommanded[capId] = value;

            var envelope = Envelope<DeviceCommandV1>.Create(
                MessageTypes.DeviceCommand,
                source: $"block:{rb.Config.Id}",
                data: new DeviceCommandV1(target, new Dictionary<string, object?> { [binding.CapabilityId] = value }),
                subject: target.ToString());
            await _bus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope, ct);
        }
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyEmitted = new Dictionary<string, object?>();

    /// <summary>
    /// Publish a block run record (Epic 1H) for the Activity Center, deduped on the emitted signature so a
    /// block that ticks every 15s only logs when its decision actually changes (or when a failing/recovering
    /// tick flips its ok state). Doubles are coarsened to one decimal for the signature so a continuously
    /// drifting monitoring output (e.g. a governor's rolling drift) doesn't churn the journal.
    /// </summary>
    private async Task PublishBlockRun(
        RunningBlock rb, bool ok, IReadOnlyDictionary<string, object?> emitted, string? detail, CancellationToken ct)
    {
        var signature = ok ? "ok|" + Signature(emitted) : "err|" + detail;
        if (rb.LastRunSignature == signature) return; // unchanged decision — nothing new to record
        rb.LastRunSignature = signature;

        var envelope = Envelope<BlockTriggeredV1>.Create(
            MessageTypes.BlockTriggered,
            source: $"block:{rb.Config.Id}",
            data: new BlockTriggeredV1(
                rb.Config.Id, rb.Config.Name, rb.Config.TypeId, DateTimeOffset.Now,
                Ok: ok, Summary: ok ? SummarizeEmitted(emitted) : "tick failed", Detail: detail),
            subject: rb.DeviceId.ToString());
        await _bus.PublishAsync(BusTopology.EventsExchange, BusTopology.BlockTriggeredKey, envelope, ct);
    }

    // Human-readable one-liner of what the block emitted this tick ("proposed_setpoint=21.5, ml_effective_stage=0").
    private static string SummarizeEmitted(IReadOnlyDictionary<string, object?> emitted) =>
        emitted.Count == 0
            ? "no output"
            : string.Join(", ", emitted.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));

    // Coarse signature for dedup — doubles rounded to 1 decimal so slowly-varying monitoring outputs settle.
    private static string Signature(IReadOnlyDictionary<string, object?> emitted) =>
        string.Join(",", emitted.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value is double d
                ? $"{kv.Key}={Math.Round(d, 1).ToString(CultureInfo.InvariantCulture)}"
                : $"{kv.Key}={FormatValue(kv.Value)}"));

    private static string FormatValue(object? v) => v switch
    {
        null => "—",
        bool b => b ? "on" : "off",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private async Task AnnounceDevice(ControlBlock config, IBlockType type, Guid deviceId, CancellationToken ct)
    {
        Guid.TryParse(config.ZoneId, out var zone);
        var descriptor = new DeviceDescriptor(
            Id: deviceId,
            Name: config.Name,
            ZoneId: zone,
            Identity: new DeviceIdentity("ControlBlock", config.Id),
            Capabilities: type.Outputs,
            Manufacturer: "Domovoy",
            Model: $"block/{type.TypeId}");

        var envelope = Envelope<DeviceDiscoveredV1>.Create(
            MessageTypes.DeviceDiscovered,
            source: $"block:{config.Id}",
            data: new DeviceDiscoveredV1(descriptor),
            subject: deviceId.ToString());
        await _bus.PublishAsync(BusTopology.DiscoveryExchange, BusTopology.DeviceDiscoveredKey, envelope, ct);
        _logger.LogInformation("Announced control block '{Name}' ({Type}) as device {DeviceId}",
            config.Name, type.TypeId, deviceId);
    }

    private Task HandleCommand(Envelope<DeviceCommandV1> envelope)
    {
        var cmd = envelope.Data;
        if (cmd is null) return Task.CompletedTask;

        // A command to a block's virtual device retargets its writable outputs (e.g. a setpoint).
        var rb = _running.Values.FirstOrDefault(r => r.DeviceId == cmd.DeviceId);
        if (rb is null) return Task.CompletedTask;

        foreach (var kv in cmd.Set)
            rb.Commanded[kv.Key] = ValueOps.Normalize(kv.Value);

        return Task.CompletedTask;
    }

    private static Guid ResolveDeviceId(ControlBlock config) =>
        Guid.TryParse(config.DeviceId, out var id) && id != Guid.Empty
            ? id
            : DeviceIdFactory.Derive("ControlBlock", config.Id);

    /// <summary>
    /// Runtime health of every loaded block for the authoring UI (roadmap Epic 1H): so a user can tell
    /// "is this block actually running?" beyond just its emitted output. A block that is enabled but has
    /// never ticked, or whose last tick threw, is surfaced here.
    /// </summary>
    public IReadOnlyList<BlockStatus> Snapshot() =>
        _running.Values
            .Select(rb => new BlockStatus(
                rb.Config.Id, rb.Config.Enabled, rb.LastTickAt, rb.TickCount,
                rb.LastEmittedCount, rb.LastError, rb.LastErrorAt))
            .ToList();

    /// <summary>Live per-instance state of a running block.</summary>
    private sealed class RunningBlock
    {
        public RunningBlock(ControlBlock config, IBlock block, Guid deviceId, DateTime configStamp)
        {
            Config = config;
            Block = block;
            DeviceId = deviceId;
            ConfigStamp = configStamp;
        }

        public ControlBlock Config { get; }
        public IBlock Block { get; }
        public Guid DeviceId { get; }
        public DateTime ConfigStamp { get; }
        public Dictionary<string, object?> State { get; } = new();
        public Dictionary<string, object?> Commanded { get; } = new();
        /// <summary>Last value sent to each bound output's target (actuation dedup, Epic 1D).</summary>
        public Dictionary<string, object?> LastCommanded { get; } = new();
        /// <summary>Last published run-record signature (Epic 1H) — dedupes the Activity Center block feed.</summary>
        public string? LastRunSignature { get; set; }

        // --- health (surfaced via Snapshot for the UI) ---
        public DateTime? LastTickAt { get; set; }
        public long TickCount { get; set; }
        public int LastEmittedCount { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorAt { get; set; }
    }

    /// <summary>Per-tick view handed to a block: inputs from the blackboard, params, commands, state.</summary>
    private sealed class BlockContext : IBlockContext
    {
        private readonly RunningBlock _rb;
        private readonly DeviceRegistry _registry;
        private readonly ZoneCache _zones;
        private readonly Dictionary<string, object?> _emitted;
        private readonly ILogger _logger;

        public BlockContext(RunningBlock rb, DeviceRegistry registry, ZoneCache zones, DateTimeOffset now,
            Dictionary<string, object?> emitted, ILogger logger)
        {
            _rb = rb;
            _registry = registry;
            _zones = zones;
            Now = now;
            _emitted = emitted;
            _logger = logger;
        }

        public DateTimeOffset Now { get; }

        public string? ZoneId => string.IsNullOrEmpty(_rb.Config.ZoneId) ? null : _rb.Config.ZoneId;

        public string? ZoneKind => _zones.KindOf(_rb.Config.ZoneId);

        public object? Read(string inputPort)
        {
            if (!_rb.Config.Inputs.TryGetValue(inputPort, out var binding)) return null;
            if (!Guid.TryParse(binding.DeviceId, out var src)) return null;
            return _registry.GetValue(src, binding.CapabilityId);
        }

        public double? ReadNumber(string inputPort) => ToDouble(Read(inputPort));

        public double Param(string key, double fallback) =>
            _rb.Config.Params.TryGetValue(key, out var v) ? v : fallback;

        public string? Option(string key) =>
            _rb.Config.Options.TryGetValue(key, out var v) ? v : null;

        public object? Commanded(string capabilityId) =>
            _rb.Commanded.TryGetValue(capabilityId, out var v) ? v : null;

        public void Emit(string capabilityId, object? value) => _emitted[capabilityId] = value;

        public T? GetState<T>(string key) =>
            _rb.State.TryGetValue(key, out var v) ? StateCoerce.As<T>(v) : default;

        public void SetState<T>(string key, T value) => _rb.State[key] = value;

        public void Log(string message) => _logger.LogInformation("[block {Id}] {Message}", _rb.Config.Id, message);

        private static double? ToDouble(object? v) => ValueOps.Normalize(v) switch
        {
            double d => d,
            bool b => b ? 1 : 0,
            _ => null,
        };
    }
}
