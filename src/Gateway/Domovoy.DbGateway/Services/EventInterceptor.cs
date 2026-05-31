using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Messaging;
using Domovoy.DbGateway.Endpoints;
using Domovoy.DbGateway.Models;
using Domovoy.MessageBus;
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
    private readonly ILogger<EventInterceptor> _logger;

    /// <summary>
    /// Recent commands per device, used to attribute a subsequent state change to the command that
    /// caused it (best-effort correlation; full replay correlation is Epic 1F).
    /// </summary>
    private readonly ConcurrentDictionary<Guid, RecentCommand> _recentCommands = new();
    private static readonly TimeSpan CommandCorrelationWindow = TimeSpan.FromSeconds(15);

    private sealed record RecentCommand(DateTime At, string TriggerSource, string? CorrelationId, IReadOnlyDictionary<string, object?> Set);

    public EventInterceptor(
        IMessageBus messageBus,
        IMongoDatabase database,
        ILogger<EventInterceptor> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure the append-only feature-store collections exist as time-series before we write.
        await TimeSeriesInitializer.EnsureCollectionsAsync(_database, _logger, stoppingToken);

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

        var logs = new List<DeviceEventLog>();
        var readings = new List<SensorReading>();
        var deviceId = report.DeviceId.ToString();

        foreach (var kv in report.State)
        {
            var newValue = Normalize(kv.Value);
            oldState.TryGetValue(kv.Key, out var oldRaw);
            var oldValue = oldRaw is null ? null : Normalize(oldRaw);

            if (ValuesEqual(oldValue, newValue)) continue; // only real deltas are events

            var commanded = correlated && recent!.Set.ContainsKey(kv.Key);
            logs.Add(new DeviceEventLog
            {
                Timestamp = DateTime.UtcNow,
                Meta = new EventMeta { DeviceId = deviceId, ZoneId = zoneId, Kind = EventKinds.StateChange },
                CapabilityId = kv.Key,
                OldValue = oldValue,
                NewValue = newValue,
                TriggerSource = commanded ? recent!.TriggerSource : TriggerSources.Device,
                CorrelationId = commanded ? recent!.CorrelationId : null,
            });

            if (TryGetDouble(newValue, out var num))
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
    };

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
