// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Domovoy.Common.Configuration;
using Domovoy.Common.Models.Events;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Home;
using Domovoy.Contracts.Messaging;
using Domovoy.DbGateway.Config;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;
using Domovoy.MessageBus;

using Microsoft.Extensions.Options;

using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Background service that persists the bus traffic to MongoDB. It maintains three things:
/// <list type="bullet">
///   <item>the <b>current-state</b> read-model (<c>capability_devices</c>) for the UI;</item>
///   <item>the append-only <b>domain event-log + telemetry</b> feature store (roadmap P0-5):
///   every capability delta and command is written to the <c>device_events</c> time-series collection
///   with its state delta and trigger source, and numeric samples to <c>sensor_readings</c>. This is
///   the fuel for ML, replay and explainability (Epics 1F/2) and is kept separate from Serilog
///   operational diagnostics; and</item>
///   <item>the <b>Zigbee bridge-liveness watchdog</b>: it tracks the coordinator's last confirmed
///   heartbeat and sweeps every Zigbee device offline while the bridge is down, so the dashboard agrees
///   with the Zigbee page instead of showing stale "online".</item>
/// </list>
/// <para>
/// The watchdog is a guest here, not a natural resident — it lives in this class only because the
/// read-model it corrects is written here. Its natural home is next to <c>DeviceLivenessWatchdog</c>;
/// moving it is a queued refactor, not an oversight.
/// </para>
/// </summary>
public class EventInterceptor : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly IMongoDatabase _database;
    private readonly TelemetryOptions _telemetry;
    private readonly ILogger<EventInterceptor> _logger;

    /// <summary>
    /// Recent commands per device, used to attribute a subsequent state change to the command that
    /// caused it (best-effort correlation; full replay correlation is Epic 1F).
    /// </summary>
    private readonly ConcurrentDictionary<Guid, RecentCommand> _recentCommands = new();
    private static readonly TimeSpan CommandCorrelationWindow = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Current home mode (roadmap Epic 1G), kept live from <see cref="HomeModeChangedV1"/> and seeded
    /// from <c>home_state</c> at startup. Stamped onto every event-log record as an ML feature (P0-5).
    /// </summary>
    private volatile string _currentMode = WellKnownModes.Default;

    /// <summary>
    /// Adapter source of Zigbee capability devices (matches <c>Zigbee2MqttAdapter.Name</c>). Used to
    /// scope the bridge-down offline sweep so only Zigbee devices are marked unreachable.
    /// </summary>
    private const string ZigbeeAdapterSource = "Zigbee2Mqtt";

    /// <summary>
    /// Last observed Zigbee bridge state (<c>null</c> until the first bridge event). The adapter re-publishes
    /// the state every ~15s; an explicit offline flips this false at once. But the offline signal is not
    /// reliable on its own — the connectivity service may restart, or the bridge may have died without a clean
    /// offline — so <see cref="IsZigbeeBridgeDown"/> also treats "no online heartbeat for a while" as down.
    /// </summary>
    private bool? _zigbeeBridgeOnline;

    /// <summary>
    /// Ticks (UTC) of the last time the Zigbee bridge was confirmed <b>online</b>. Seeded to startup so a cold
    /// start gets a grace window before the watchdog can expire anything. Stored as ticks for atomic access
    /// from the bus handlers and the watchdog loop.
    /// </summary>
    private long _lastBridgeOnlineTicks = DateTime.UtcNow.Ticks;

    /// <summary>
    /// How long the bridge may go without an <b>online</b> confirmation before its devices are treated as
    /// offline. The adapter re-emits online every ~15s while the bridge is healthy, so this is several missed
    /// heartbeats — long enough that a brief connectivity restart doesn't false-offline live Zigbee devices.
    /// </summary>
    private static readonly TimeSpan BridgeStaleTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How often the Zigbee bridge-liveness watchdog re-checks and reconciles.</summary>
    private static readonly TimeSpan BridgeWatchdogInterval = TimeSpan.FromSeconds(30);

    private sealed record RecentCommand(
        DateTime At, string TriggerSource, string? TriggerId, string? CorrelationId, IReadOnlyDictionary<string, object?> Set);

    public EventInterceptor(
        IMessageBus messageBus,
        IMongoDatabase database,
        IOptions<TelemetryOptions> telemetry,
        ILogger<EventInterceptor> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _telemetry = telemetry.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure the append-only feature-store collections exist as time-series before we write, and
        // apply the raw-telemetry retention policy (Epic 1B).
        await TimeSeriesInitializer.EnsureCollectionsAsync(
            _database, _logger, _telemetry.RawRetentionDays, stoppingToken);

        // Seed the home mode (1G) so events logged before the first switch carry the right context.
        await SeedCurrentMode();

        _logger.LogInformation("EventInterceptor starting — subscribing to capability contract events");

        // --- Current-state read-model + event-log (roadmap Step 4 + P0-5) ---
        await _messageBus.SubscribeAsync<Envelope<DeviceDiscoveredV1>>(
            "dbgateway-capability-discovery",
            BusTopology.DiscoveryExchange,
            BusTopology.DeviceDiscoveredKey,
            HandleCapabilityDiscovered);

        await _messageBus.SubscribeAsync<Envelope<DeviceStateReportV1>>(
            "dbgateway-capability-state",
            BusTopology.StateExchange,
            BusTopology.DeviceStateUpdatedKey,
            HandleCapabilityState);

        await _messageBus.SubscribeAsync<Envelope<DeviceOnlineChangedV1>>(
            "dbgateway-capability-online",
            BusTopology.EventsExchange,
            BusTopology.DeviceOnlineChangedKey,
            HandleCapabilityOnline);

        // Zigbee bridge state (coordinator/stick up-down). Per-device liveness normally arrives via
        // zigbee2mqtt availability (adapter → DeviceOnlineChangedV1, handled above), but that channel is
        // silent when the bridge process itself is down — z2m can't report its devices offline if it isn't
        // running. This bridge-down sweep is the backstop for that whole-bridge-down case: when the bridge
        // drops we mark every Zigbee device offline so the dashboard agrees with the Zigbee page.
        await _messageBus.SubscribeAsync<ZigbeeBridgeStateEvent>(
            "dbgateway-zigbee-bridge-state",
            MessageBusConfiguration.ZigbeeBridgeExchange,
            MessageBusConfiguration.ZigbeeBridgeStateRoutingKey,
            HandleZigbeeBridgeState);

        // Commands are logged for audit and to attribute later state changes to a trigger (P0-5).
        await _messageBus.SubscribeAsync<Envelope<DeviceCommandV1>>(
            "dbgateway-command-log",
            BusTopology.CommandsExchange,
            BusTopology.DeviceCommandKey,
            HandleDeviceCommand);

        // Automation run history (Epic 1A) — persisted from AutomationTriggeredV1.
        await _messageBus.SubscribeAsync<Envelope<AutomationTriggeredV1>>(
            "dbgateway-automation-history",
            BusTopology.EventsExchange,
            BusTopology.AutomationTriggeredKey,
            HandleAutomationTriggered);

        // Control-block run history (Epic 1H) — persisted from BlockTriggeredV1 for the 'block' Activity source.
        await _messageBus.SubscribeAsync<Envelope<BlockTriggeredV1>>(
            "dbgateway-block-history",
            BusTopology.EventsExchange,
            BusTopology.BlockTriggeredKey,
            HandleBlockTriggered);

        // Home mode changes (Epic 1G) — track the current mode and record the change in the event-log.
        await _messageBus.SubscribeAsync<Envelope<HomeModeChangedV1>>(
            "dbgateway-home-mode",
            BusTopology.EventsExchange,
            BusTopology.HomeModeChangedKey,
            HandleHomeModeChanged);

        // Backstop for Zigbee liveness: expire devices when the bridge stops confirming itself online, without
        // depending on the offline signal continuing to flow (see ZigbeeBridgeWatchdogAsync).
        _ = Task.Run(() => ZigbeeBridgeWatchdogAsync(stoppingToken), stoppingToken);

        _logger.LogInformation("EventInterceptor subscriptions complete");
    }

    // ====================================================================
    // Current-state read-model persistence (roadmap Step 4)
    // ====================================================================

    private const string CapabilityCollection = "capability_devices";

    private async Task HandleCapabilityDiscovered(Envelope<DeviceDiscoveredV1> envelope)
    {
        var device = envelope.Data?.Device;
        if (device is null) return;

        try
        {
            var collection = _database.GetCollection<CapabilityDeviceDocument>(CapabilityCollection);
            var filter = Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.Id, device.Id.ToString());
            // The announce rewrites the whole capability list, so the platform's synthetic power/energy series
            // (Epic 3C-D) has to be re-applied from the stored energy profile — otherwise a re-announce would
            // silently drop a tracked device out of the accounting.
            var existing = await collection.Find(x => x.Id == device.Id.ToString()).FirstOrDefaultAsync();
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.Name, device.Name)
                .Set(x => x.AdapterSource, device.Identity.AdapterSource)
                .Set(x => x.Model, device.Model)
                .Set(x => x.Capabilities, SyntheticCapabilities.Apply(
                    device.Capabilities.Select(ToCapabilityDocument), existing?.EnergyProfile))
                // While the Zigbee bridge is down, a re-delivered RETAINED discovery must not flip the device
                // back online (the broker replays z2m's last device list even though z2m can't reach it).
                .Set(x => x.IsOnline, ResolveOnline(device.Identity.AdapterSource))
                // Semantic archetype (Epic 2D): recompute the auto value each (re)announce; the manual
                // override field is left untouched so a re-announce never clobbers the user's choice.
                .Set(x => x.AutoArchetype, DeviceClassifier.Classify(device.Capabilities, device.Identity.AdapterSource, device.Model))
                .Set(x => x.LastUpdated, DateTime.UtcNow)
                // ZoneId is a manual, UI-assigned read-model concern (P0-3). Adapters always announce
                // Guid.Empty, so it is set only on first insert — a re-announce must not wipe the zone.
                .SetOnInsert(x => x.ZoneId, device.ZoneId == Guid.Empty ? string.Empty : device.ZoneId.ToString())
                .SetOnInsert(x => x.State, new Dictionary<string, object>());

            await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
            _logger.LogInformation("Persisted capability device {DeviceId} ({Name})", device.Id, device.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting capability device {DeviceId}", device.Id);
        }
    }

    private async Task HandleCapabilityState(Envelope<DeviceStateReportV1> envelope)
    {
        var report = envelope.Data;
        if (report is null || report.State.Count == 0) return;

        try
        {
            var collection = _database.GetCollection<CapabilityDeviceDocument>(CapabilityCollection);
            var existing = await collection.Find(x => x.Id == report.DeviceId.ToString()).FirstOrDefaultAsync();
            var zoneId = existing?.ZoneId ?? string.Empty;
            var oldState = existing?.State ?? new Dictionary<string, object>();

            // Append the event-log delta + telemetry BEFORE overwriting the read-model state (P0-5).
            await RecordStateDeltas(report, envelope.Source, zoneId, oldState, existing);

            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            // The platform's own energy estimate (Epic 3C-D) is published on behalf of the device and says
            // nothing about its reachability — only a report the device itself produced may (re)online it.
            // A retained state message during a bridge outage must not re-online a Zigbee device (see discovery).
            if (!IsEstimatorReport(envelope.Source))
                update = update.Set(x => x.IsOnline, ResolveOnline(existing?.AdapterSource));
            foreach (var kv in report.State)
                update = update.Set($"State.{kv.Key}", Normalize(kv.Value));

            await collection.UpdateOneAsync(
                x => x.Id == report.DeviceId.ToString(), update, new UpdateOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting capability state for {DeviceId}", report.DeviceId);
        }
    }

    private async Task HandleCapabilityOnline(Envelope<DeviceOnlineChangedV1> envelope)
    {
        var change = envelope.Data;
        if (change is null) return;

        try
        {
            var collection = _database.GetCollection<CapabilityDeviceDocument>(CapabilityCollection);
            var filter = Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.Id, change.DeviceId.ToString());
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.IsOnline, change.IsOnline)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            await collection.UpdateOneAsync(filter, update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating availability for {DeviceId}", change.DeviceId);
        }
    }

    /// <summary>
    /// Tracks the Zigbee bridge state and, on offline, sweeps its devices unreachable. The adapter re-emits the
    /// state ~15s; an online event refreshes the liveness stamp (feeding <see cref="IsZigbeeBridgeDown"/>), an
    /// offline event flips every Zigbee device offline right away. The sweep runs on EVERY offline (not just the
    /// online→offline edge) so a retained discovery/state the broker replays can't leave a device stuck online.
    /// </summary>
    private async Task HandleZigbeeBridgeState(ZigbeeBridgeStateEvent ev)
    {
        _zigbeeBridgeOnline = ev.IsOnline;
        if (ev.IsOnline)
        {
            Interlocked.Exchange(ref _lastBridgeOnlineTicks, DateTime.UtcNow.Ticks);
            return; // devices re-online on their own as they re-report once the bridge is back
        }

        await SweepZigbeeOffline("bridge reported offline");
    }

    /// <summary>
    /// Marks currently-online Zigbee devices offline. Cheap + idempotent — the filter matches only rows that are
    /// still <c>IsOnline=true</c>, so once the house has settled offline a repeat sweep updates nothing.
    /// </summary>
    private async Task SweepZigbeeOffline(string reason)
    {
        try
        {
            var collection = _database.GetCollection<CapabilityDeviceDocument>(CapabilityCollection);
            var filter = Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.AdapterSource, ZigbeeAdapterSource)
                & Builders<CapabilityDeviceDocument>.Filter.Eq(x => x.IsOnline, true);
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.IsOnline, false)
                .Set(x => x.LastUpdated, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(filter, update);
            if (result.ModifiedCount > 0)
                _logger.LogInformation(
                    "Zigbee bridge down ({Reason}) — marked {Count} Zigbee device(s) offline", reason, result.ModifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sweeping Zigbee devices offline");
        }
    }

    /// <summary>
    /// The Zigbee bridge is treated as down when it explicitly reported offline, OR it has not confirmed itself
    /// online within <see cref="BridgeStaleTimeout"/>. The latter is what makes liveness robust to the offline
    /// signal drying up (connectivity restarts, an unclean coordinator death, a stale retained state): the bridge
    /// heartbeats online every ~15s while healthy, so a lapse means it is not actually serving its devices.
    /// </summary>
    private bool IsZigbeeBridgeDown() =>
        _zigbeeBridgeOnline == false
        || DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastBridgeOnlineTicks), DateTimeKind.Utc) > BridgeStaleTimeout;

    /// <summary>
    /// The <c>IsOnline</c> value a discovery/state report should persist. Normally <c>true</c> (a report means
    /// the device is reachable), but for a <b>Zigbee</b> device while the bridge is down it stays <c>false</c>:
    /// with the coordinator unreachable z2m can't actually talk to the device, and the only "reports" are stale
    /// RETAINED messages the broker replays on (re)subscribe. Without this gate such a replay would flip a dead
    /// device back online right after the bridge-down sweep.
    /// </summary>
    private bool ResolveOnline(string? adapterSource) =>
        !(string.Equals(adapterSource, ZigbeeAdapterSource, StringComparison.OrdinalIgnoreCase) && IsZigbeeBridgeDown());

    /// <summary>
    /// Backstop watchdog: periodically expires Zigbee devices when the bridge is down. Unlike the event-driven
    /// sweep it does not need the offline signal to keep flowing — a bridge that stops confirming itself online
    /// (crash-loop with no coordinator, connectivity down, unclean death) is caught by the staleness in
    /// <see cref="IsZigbeeBridgeDown"/>. A startup grace window lets a genuinely-live bridge heartbeat first, so
    /// a cold start doesn't briefly offline healthy Zigbee devices.
    /// </summary>
    private async Task ZigbeeBridgeWatchdogAsync(CancellationToken token)
    {
        try { await Task.Delay(BridgeStaleTimeout, token); }
        catch (OperationCanceledException) { return; }

        while (!token.IsCancellationRequested)
        {
            if (IsZigbeeBridgeDown())
                await SweepZigbeeOffline("no online heartbeat");

            try { await Task.Delay(BridgeWatchdogInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ====================================================================
    // Append-only feature store: event-log + telemetry (roadmap P0-5)
    // ====================================================================

    private async Task HandleDeviceCommand(Envelope<DeviceCommandV1> envelope)
    {
        var cmd = envelope.Data;
        if (cmd is null || cmd.Set.Count == 0) return;

        var (trigger, triggerId) = ParseTrigger(envelope.Source);
        var correlationId = envelope.CorrelationId ?? envelope.Id;
        var normalizedSet = cmd.Set.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));

        // Remember the command so a state change arriving shortly after can be attributed to it.
        _recentCommands[cmd.DeviceId] = new RecentCommand(DateTime.UtcNow, trigger, triggerId, correlationId,
            normalizedSet.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));

        try
        {
            var zoneId = await LookupZoneId(cmd.DeviceId);
            var records = normalizedSet.Select(kv => new DeviceEventLog
            {
                Timestamp = DateTime.UtcNow,
                Meta = new EventMeta { DeviceId = cmd.DeviceId.ToString(), ZoneId = zoneId, Kind = EventKinds.Command },
                CapabilityId = kv.Key,
                NewValue = kv.Value,
                TriggerSource = trigger,
                TriggerId = triggerId,
                RuleId = trigger == TriggerSources.Rule ? triggerId : null,
                Mode = _currentMode,
                CorrelationId = correlationId,
            }).ToList();

            await EventLog.InsertManyAsync(records);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging command for {DeviceId}", cmd.DeviceId);
        }
    }

    private async Task RecordStateDeltas(
        DeviceStateReportV1 report,
        string? reportSource,
        string zoneId,
        IReadOnlyDictionary<string, object> oldState,
        CapabilityDeviceDocument? existing)
    {
        var correlated = _recentCommands.TryGetValue(report.DeviceId, out var recent)
            && (DateTime.UtcNow - recent!.At) <= CommandCorrelationWindow;

        // A block's virtual device reports its own outputs with source "block:{id}" (Epic 1H BlockRuntime).
        // Without this, a governor's proposed_setpoint / ml_drift deltas would be attributed "by device",
        // indistinguishable from a sensor. Adapters report with their own name → stays Device.
        var isBlockReport = reportSource?.StartsWith("block", StringComparison.OrdinalIgnoreCase) == true;
        var uncorrelatedSource = isBlockReport ? TriggerSources.Block : TriggerSources.Device;
        var uncorrelatedId = isBlockReport ? SourceId(reportSource!) : null;

        // System virtual sensors (Epic 2L) report continuously-varying values every minute (sun
        // elevation/azimuth, clock/time_of_day, date). Those would swamp the activity event-log with
        // meaningless per-tick deltas, so for a System device we log ONLY discrete capabilities
        // (Boolean/Enum: is_dark, is_day, is_weekend, is_holiday, day_of_week) — the genuinely meaningful
        // "it became dark / it's a new day" moments. Numeric values still go to telemetry (useful trends);
        // text values (clock, sunrise, date) update only the read-model.
        var isSystem = string.Equals(existing?.AdapterSource, "System", StringComparison.OrdinalIgnoreCase);

        // The energy estimator (Epic 3C-D) republishes a device's derived power/energy on a timer. Those are
        // trends, not events — they go to telemetry below, but never into the activity journal.
        var isEstimator = IsEstimatorReport(reportSource);

        var logs = new List<DeviceEventLog>();
        var readings = new List<SensorReading>();
        var deviceId = report.DeviceId.ToString();

        foreach (var kv in report.State)
        {
            var newValue = Normalize(kv.Value);
            oldState.TryGetValue(kv.Key, out var oldRaw);
            var oldValue = oldRaw is null ? null : Normalize(oldRaw);

            if (ValuesEqual(oldValue, newValue)) continue; // only real deltas are events

            var isNumeric = TryGetDouble(newValue, out var num);

            if (isNumeric)
                readings.Add(new SensorReading
                {
                    Timestamp = DateTime.UtcNow,
                    Meta = new TelemetryMeta
                    {
                        DeviceId = deviceId,
                        ZoneId = zoneId,
                        CapabilityId = kv.Key,
                        Unit = UnitOf(existing, kv.Key),
                    },
                    Value = num,
                });

            if (isEstimator) continue;
            // For a System sensor, only discrete (Boolean/Enum) transitions belong in the activity log.
            if (isSystem && !IsDiscreteCapability(existing, kv.Key)) continue;
            // The Home device mirrors the home mode as a capability (device→mode bridge); the canonical
            // journal record for a switch is the mode_change event — don't log the mirror's delta twice.
            if (isSystem && kv.Key == ContextCapabilities.HomeMode) continue;

            var commanded = correlated && recent!.Set.ContainsKey(kv.Key);
            logs.Add(new DeviceEventLog
            {
                Timestamp = DateTime.UtcNow,
                Meta = new EventMeta { DeviceId = deviceId, ZoneId = zoneId, Kind = EventKinds.StateChange },
                CapabilityId = kv.Key,
                OldValue = oldValue,
                NewValue = newValue,
                TriggerSource = commanded ? recent!.TriggerSource : uncorrelatedSource,
                TriggerId = commanded ? recent!.TriggerId : uncorrelatedId,
                RuleId = commanded && recent!.TriggerSource == TriggerSources.Rule ? recent.TriggerId : null,
                Mode = _currentMode,
                CorrelationId = commanded ? recent!.CorrelationId : null,
            });
        }

        if (logs.Count > 0) await EventLog.InsertManyAsync(logs);
        if (readings.Count > 0) await Telemetry.InsertManyAsync(readings);
    }

    private async Task HandleAutomationTriggered(Envelope<AutomationTriggeredV1> envelope)
    {
        var e = envelope.Data;
        if (e is null) return;

        try
        {
            await _database.GetCollection<AutoHistory>(AutomationEndpoints.HistoryCollection).InsertOneAsync(new AutoHistory
            {
                Timestamp = e.FiredAt.UtcDateTime,
                RuleId = e.RuleId,
                RuleName = e.RuleName,
                ConditionsMet = e.ConditionsMet,
                Success = e.Success,
                TriggerSummary = e.TriggerSummary,
                ActionsExecuted = e.ActionsExecuted,
                Detail = e.Detail,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting automation history for rule {RuleId}", e.RuleId);
        }
    }

    private async Task HandleBlockTriggered(Envelope<BlockTriggeredV1> envelope)
    {
        var e = envelope.Data;
        if (e is null) return;

        try
        {
            await _database.GetCollection<BlockHistory>(BlockHistory.Collection).InsertOneAsync(new BlockHistory
            {
                Timestamp = e.TickedAt.UtcDateTime,
                BlockId = e.BlockId,
                BlockName = e.BlockName,
                TypeId = e.TypeId,
                Ok = e.Ok,
                Summary = e.Summary,
                Detail = e.Detail,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting block history for block {BlockId}", e.BlockId);
        }
    }

    // ====================================================================
    // Home mode / presence context (roadmap Epic 1G)
    // ====================================================================

    private async Task SeedCurrentMode()
    {
        try
        {
            var state = await _database.GetCollection<HomeState>(ModeEndpoints.Collection)
                .Find(x => x.Id == HomeState.SingletonId)
                .FirstOrDefaultAsync();
            if (state is not null && !string.IsNullOrWhiteSpace(state.Mode))
                _currentMode = state.Mode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not seed current home mode; defaulting to {Mode}", _currentMode);
        }
    }

    private async Task HandleHomeModeChanged(Envelope<HomeModeChangedV1> envelope)
    {
        var change = envelope.Data;
        if (change is null || string.IsNullOrWhiteSpace(change.Mode)) return;

        _currentMode = change.Mode;

        // Record the mode change itself as a feature-store event so history/replay can reconstruct context.
        try
        {
            var (trigger, triggerId) = ParseModeTrigger(change.Source);
            await EventLog.InsertOneAsync(new DeviceEventLog
            {
                Timestamp = change.ChangedAt.UtcDateTime,
                Meta = new EventMeta { Kind = EventKinds.ModeChange },
                CapabilityId = ContextCapabilities.HomeMode,
                OldValue = change.PreviousMode,
                NewValue = change.Mode,
                TriggerSource = trigger,
                TriggerId = triggerId,
                RuleId = trigger == TriggerSources.Rule ? triggerId : null,
                Mode = change.Mode,
            });
            _logger.LogInformation("Home mode changed to {Mode} (was {Previous}, by {Source})",
                change.Mode, change.PreviousMode ?? "—", change.Source);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording home-mode change to {Mode}", change.Mode);
        }
    }

    /// <summary>
    /// Parse a mode-change source into (trigger, initiator id). Presence-driven switches used to be
    /// collapsed into <c>device</c> — masking the culprit. Today the primary automated path is the
    /// <c>presence_mode</c> control block commanding the Home virtual device (source <c>block:{id}</c>,
    /// passed through the device→mode bridge); the <c>presence:{deviceId}</c> form stays recognized for
    /// any other presence-shaped sender. A user switch may carry the self-declared user id (<c>user:{id}</c>).
    /// </summary>
    private static (string Kind, string? Id) ParseModeTrigger(string? source)
    {
        var s = source?.ToLowerInvariant() ?? string.Empty;
        var id = source is null ? null : SourceId(source);
        if (s.StartsWith("block")) return (TriggerSources.Block, id);
        if (s.Contains("ml")) return (TriggerSources.Ml, id);
        if (s.Contains("rule") || s.Contains("automation")) return (TriggerSources.Rule, id);
        if (s.Contains("presence")) return (TriggerSources.Presence, id);
        if (s.Contains("device")) return (TriggerSources.Device, id);
        return (TriggerSources.User, id);
    }

    private IMongoCollection<DeviceEventLog> EventLog =>
        _database.GetCollection<DeviceEventLog>(TimeSeriesInitializer.DeviceEventsCollection);

    private IMongoCollection<SensorReading> Telemetry =>
        _database.GetCollection<SensorReading>(TimeSeriesInitializer.SensorReadingsCollection);

    private async Task<string> LookupZoneId(Guid deviceId)
    {
        var doc = await _database.GetCollection<CapabilityDeviceDocument>(CapabilityCollection)
            .Find(x => x.Id == deviceId.ToString())
            .Project(x => x.ZoneId)
            .FirstOrDefaultAsync();
        return doc ?? string.Empty;
    }

    /// <summary>
    /// True for a state report the platform's energy estimator published on a device's behalf (Epic 3C-D,
    /// source <c>energy:{deviceId}</c>). Such a report carries only the derived power/energy series: it must
    /// not affect reachability, and its numeric churn belongs in telemetry rather than the activity journal.
    /// </summary>
    private static bool IsEstimatorReport(string? source) =>
        source?.StartsWith(EstimatorSourcePrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Actor-string prefix the AutomationService energy estimator publishes with (Epic 3C-D).</summary>
    public const string EstimatorSourcePrefix = "energy:";

    private static string? UnitOf(CapabilityDeviceDocument? device, string capabilityId) =>
        device?.Capabilities.FirstOrDefault(c => c.Id == capabilityId)?.Unit;

    /// <summary>True for a discrete (Boolean/Enum) capability — the only kinds a System sensor logs to the
    /// activity event-log (its numeric/text values churn every minute; see <see cref="RecordStateDeltas"/>).</summary>
    private static bool IsDiscreteCapability(CapabilityDeviceDocument? device, string capabilityId)
    {
        var kind = device?.Capabilities.FirstOrDefault(c => c.Id == capabilityId)?.Kind;
        return string.Equals(kind, nameof(CapabilityKind.Boolean), StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, nameof(CapabilityKind.Enum), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parse the actor-string convention in <c>Envelope.Source</c> — <c>{kind}:{id}</c> — into the coarse
    /// trigger bucket plus the concrete initiator id: <c>automation:{ruleId}</c> → (rule, ruleId),
    /// <c>block:{id}</c> → (block, id), <c>user:{userId}</c> (self-declared, Phase 3 auth pending) →
    /// (user, userId). A bare <c>apigateway</c> stays an anonymous user command.
    /// </summary>
    private static (string Kind, string? Id) ParseTrigger(string? source)
    {
        if (string.IsNullOrEmpty(source)) return (TriggerSources.User, null);
        var s = source.ToLowerInvariant();
        var id = SourceId(source);
        // Control-block actuation is published as source "block:{id}" (Epic 1H BlockRuntime). Attribute it
        // to the block, not the user, so a thermostat/sequencer loop is distinguishable from manual actions.
        if (s.StartsWith("block")) return (TriggerSources.Block, id);
        // A scene activation is published as source "scene:{id}" (Epic 3B ScenesController). Attribute it to
        // the scene (id = scene id) so scene-schedule discovery (Epic 2F) can see "which scene was activated
        // when" instead of it collapsing into an anonymous user change.
        if (s.StartsWith("scene")) return (TriggerSources.Scene, id);
        if (s.Contains("automation") || s.Contains("rule")) return (TriggerSources.Rule, id);
        if (s.StartsWith("user")) return (TriggerSources.User, id);
        if (s.Contains("ml")) return (TriggerSources.Ml, id);
        // Commands today originate from user actions through the gateway.
        return (TriggerSources.User, null);
    }

    /// <summary>The id part of an actor-string (<c>kind:id</c>), or null when there is none.</summary>
    private static string? SourceId(string source)
    {
        var idx = source.IndexOf(':');
        if (idx < 0 || idx == source.Length - 1) return null;
        var id = source[(idx + 1)..].Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static CapabilityDocument ToCapabilityDocument(Capability c) => new()
    {
        Id = c.Id,
        Kind = c.Kind.ToString(),
        Writable = AttrBool(c.Attributes, CapabilityAttributeKeys.Writable),
        Unit = AttrString(c.Attributes, CapabilityAttributeKeys.Unit),
        Min = AttrNumber(c.Attributes, CapabilityAttributeKeys.Min),
        Max = AttrNumber(c.Attributes, CapabilityAttributeKeys.Max),
        Step = AttrNumber(c.Attributes, CapabilityAttributeKeys.Step),
        Values = AttrStringArray(c.Attributes, CapabilityAttributeKeys.Values),
        Editor = AttrString(c.Attributes, CapabilityAttributeKeys.Editor),
    };

    /// <summary>Reads a string[] attribute (Enum <c>values</c>), robust to a JsonElement array over the bus.</summary>
    private static List<string>? AttrStringArray(IReadOnlyDictionary<string, object?> attrs, string key)
    {
        if (!attrs.TryGetValue(key, out var v) || v is null) return null;
        switch (v)
        {
            case IEnumerable<string> ss:
                return ss.ToList();
            case JsonElement e when e.ValueKind == JsonValueKind.Array:
                return e.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToList();
            default:
                return null;
        }
    }

    private static bool AttrBool(IReadOnlyDictionary<string, object?> attrs, string key) =>
        attrs.TryGetValue(key, out var v) && v switch
        {
            bool b => b,
            JsonElement e => e.ValueKind == JsonValueKind.True,
            _ => false
        };

    private static string? AttrString(IReadOnlyDictionary<string, object?> attrs, string key) =>
        attrs.TryGetValue(key, out var v)
            ? v switch
            {
                string s => s,
                JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
                _ => null
            }
            : null;

    private static double? AttrNumber(IReadOnlyDictionary<string, object?> attrs, string key) =>
        attrs.TryGetValue(key, out var v)
            ? v switch
            {
                double d => d,
                int i => i,
                long l => l,
                JsonElement e when e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) => d,
                _ => (double?)null
            }
            : null;

    private static object Normalize(object? v) => v switch
    {
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.String => e.GetString() ?? string.Empty,
            _ => string.Empty
        },
        null => string.Empty,
        _ => v
    };

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (TryGetDouble(a, out var da) && TryGetDouble(b, out var db)) return da.Equals(db);
        return a.Equals(b);
    }

    private static bool TryGetDouble(object? v, out double result)
    {
        switch (v)
        {
            case double d: result = d; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p):
                result = p; return true;
            default: result = 0; return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventInterceptor stopping");
        await base.StopAsync(cancellationToken);
    }
}
