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
/// Background service that persists the bus traffic to MongoDB. It maintains two things:
/// <list type="bullet">
///   <item>the <b>current-state</b> read-model (<c>capability_devices</c>) for the UI; and</item>
///   <item>the append-only <b>domain event-log + telemetry</b> feature store (roadmap P0-5):
///   every capability delta and command is written to the <c>device_events</c> time-series collection
///   with its state delta and trigger source, and numeric samples to <c>sensor_readings</c>. This is
///   the fuel for ML, replay and explainability (Epics 1F/2) and is kept separate from Serilog
///   operational diagnostics.</item>
/// </list>
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
    /// Last observed Zigbee bridge state. The adapter re-publishes the state every ~15s, so we only act
    /// on the online→offline transition (or the first offline seen) to avoid rewriting the read-model
    /// on every heartbeat. <c>null</c> until the first bridge event arrives.
    /// </summary>
    private bool? _zigbeeBridgeOnline;

    private sealed record RecentCommand(DateTime At, string TriggerSource, string? CorrelationId, IReadOnlyDictionary<string, object?> Set);

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

        // Zigbee bridge state (coordinator/stick up-down). The Zigbee2Mqtt adapter never emits a
        // per-device DeviceOnlineChangedV1, so a disconnected stick would otherwise leave every paired
        // device stuck as IsOnline=true in the read-model. When the bridge drops we sweep all Zigbee
        // devices offline so the dashboard agrees with the (staleness-based) Zigbee page.
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

        // Home mode changes (Epic 1G) — track the current mode and record the change in the event-log.
        await _messageBus.SubscribeAsync<Envelope<HomeModeChangedV1>>(
            "dbgateway-home-mode",
            BusTopology.EventsExchange,
            BusTopology.HomeModeChangedKey,
            HandleHomeModeChanged);

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
            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.Name, device.Name)
                .Set(x => x.AdapterSource, device.Identity.AdapterSource)
                .Set(x => x.Model, device.Model)
                .Set(x => x.Capabilities, device.Capabilities.Select(ToCapabilityDocument).ToList())
                .Set(x => x.IsOnline, true)
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
            await RecordStateDeltas(report, zoneId, oldState, existing);

            var update = Builders<CapabilityDeviceDocument>.Update
                .Set(x => x.IsOnline, true)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
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
    /// Marks all Zigbee devices offline when the bridge (coordinator/stick) goes down. Acts only on the
    /// online→offline transition — the adapter re-emits the state on a ~15s heartbeat, so a naive handler
    /// would rewrite the collection repeatedly. Devices flip back to online on their own as they re-report
    /// state/discovery once the bridge returns, so the online transition needs no action here.
    /// </summary>
    private async Task HandleZigbeeBridgeState(ZigbeeBridgeStateEvent ev)
    {
        var wasOnline = _zigbeeBridgeOnline;
        _zigbeeBridgeOnline = ev.IsOnline;

        // Only sweep on the first offline we see or a true→false edge. Ignore the online transition
        // and the repeated offline heartbeats.
        if (ev.IsOnline || wasOnline == false) return;

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
                    "Zigbee bridge offline — marked {Count} Zigbee device(s) offline", result.ModifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sweeping Zigbee devices offline after bridge drop");
        }
    }

    // ====================================================================
    // Append-only feature store: event-log + telemetry (roadmap P0-5)
    // ====================================================================

    private async Task HandleDeviceCommand(Envelope<DeviceCommandV1> envelope)
    {
        var cmd = envelope.Data;
        if (cmd is null || cmd.Set.Count == 0) return;

        var trigger = MapTriggerSource(envelope.Source);
        var correlationId = envelope.CorrelationId ?? envelope.Id;
        var normalizedSet = cmd.Set.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));

        // Remember the command so a state change arriving shortly after can be attributed to it.
        _recentCommands[cmd.DeviceId] = new RecentCommand(DateTime.UtcNow, trigger, correlationId,
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
        string zoneId,
        IReadOnlyDictionary<string, object> oldState,
        CapabilityDeviceDocument? existing)
    {
        var correlated = _recentCommands.TryGetValue(report.DeviceId, out var recent)
            && (DateTime.UtcNow - recent!.At) <= CommandCorrelationWindow;

        // System virtual sensors (Epic 2L) report continuously-varying values every minute (sun
        // elevation/azimuth, clock/time_of_day, date). Those would swamp the activity event-log with
        // meaningless per-tick deltas, so for a System device we log ONLY discrete capabilities
        // (Boolean/Enum: is_dark, is_day, is_weekend, is_holiday, day_of_week) — the genuinely meaningful
        // "it became dark / it's a new day" moments. Numeric values still go to telemetry (useful trends);
        // text values (clock, sunrise, date) update only the read-model.
        var isSystem = string.Equals(existing?.AdapterSource, "System", StringComparison.OrdinalIgnoreCase);

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

            // For a System sensor, only discrete (Boolean/Enum) transitions belong in the activity log.
            if (isSystem && !IsDiscreteCapability(existing, kv.Key)) continue;

            var commanded = correlated && recent!.Set.ContainsKey(kv.Key);
            logs.Add(new DeviceEventLog
            {
                Timestamp = DateTime.UtcNow,
                Meta = new EventMeta { DeviceId = deviceId, ZoneId = zoneId, Kind = EventKinds.StateChange },
                CapabilityId = kv.Key,
                OldValue = oldValue,
                NewValue = newValue,
                TriggerSource = commanded ? recent!.TriggerSource : TriggerSources.Device,
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
            await EventLog.InsertOneAsync(new DeviceEventLog
            {
                Timestamp = change.ChangedAt.UtcDateTime,
                Meta = new EventMeta { Kind = EventKinds.ModeChange },
                CapabilityId = ContextCapabilities.HomeMode,
                OldValue = change.PreviousMode,
                NewValue = change.Mode,
                TriggerSource = MapModeSource(change.Source),
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

    private static string MapModeSource(string? source)
    {
        var s = source?.ToLowerInvariant() ?? string.Empty;
        if (s.Contains("ml")) return TriggerSources.Ml;
        if (s.Contains("rule") || s.Contains("automation")) return TriggerSources.Rule;
        if (s.Contains("presence") || s.Contains("device")) return TriggerSources.Device;
        return TriggerSources.User;
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

    private static string MapTriggerSource(string? source)
    {
        if (string.IsNullOrEmpty(source)) return TriggerSources.User;
        var s = source.ToLowerInvariant();
        if (s.Contains("automation") || s.Contains("rule")) return TriggerSources.Rule;
        if (s.Contains("ml")) return TriggerSources.Ml;
        // Commands today originate from user actions through the gateway.
        return TriggerSources.User;
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
