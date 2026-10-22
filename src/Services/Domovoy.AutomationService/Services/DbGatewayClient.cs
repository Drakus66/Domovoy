// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Blocks;
using Domovoy.Contracts.Capabilities;
using Domovoy.Contracts.Ml;
using Domovoy.Contracts.Proposals;
using Domovoy.Contracts.Scenes;

namespace Domovoy.AutomationService.Services;

/// <summary>
/// Reads the DbGateway HTTP API: user automation rules and the device read-model (for zone + initial
/// state). Kept tolerant — a transient gateway outage must not crash the engine, it just keeps the last
/// good rule set (offline-first; safety-floor rules are local anyway).
/// </summary>
public sealed class DbGatewayClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DbGatewayClient> _logger;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DbGatewayClient(HttpClient http, ILogger<DbGatewayClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<AutomationRule>?> GetUserRulesAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<AutomationRule>>("api/automations", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load user rules from DbGateway");
            return null;
        }
    }

    /// <summary>Global variables (roadmap Epic 3E), or null if the gateway is unreachable.</summary>
    public async Task<List<GlobalVariable>?> GetVariablesAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<GlobalVariable>>("api/variables", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load global variables from DbGateway");
            return null;
        }
    }

    /// <summary>
    /// Persist a variable's value (Epic 3E) — durable, since (unlike the Power device's live signal) a
    /// variable's whole point is surviving a restart. Best-effort: a failure just delays persistence,
    /// matching <see cref="SaveBlockStateAsync"/>.
    /// </summary>
    public async Task SetVariableValueAsync(string id, object? value, CancellationToken ct)
    {
        try
        {
            await _http.PutAsJsonAsync($"api/variables/{Uri.EscapeDataString(id)}/value",
                new VariableValueUpdate(value), Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist value for variable {VariableId}", id);
        }
    }

    private sealed record VariableValueUpdate(object? Value);

    /// <summary>Stored scenes (Epic 3B) so the <c>scene</c> rule action can resolve targets; null if unreachable.</summary>
    public async Task<List<Scene>?> GetScenesAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<Scene>>("api/scenes", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load scenes from DbGateway");
            return null;
        }
    }

    /// <summary>Control-block instance configs (Epic 1H), or null if the gateway is unreachable.</summary>
    public async Task<List<ControlBlock>?> GetBlocksAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ControlBlock>>("api/blocks", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load control blocks from DbGateway");
            return null;
        }
    }

    /// <summary>Persisted block runtime state (Epic 2Q, Phase 2), or null if the gateway is unreachable.</summary>
    public async Task<List<BlockStateRecord>?> GetBlockStatesAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<BlockStateRecord>>("api/block-state", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load block state from DbGateway");
            return null;
        }
    }

    /// <summary>Snapshot one block's state (Epic 2Q, Phase 2). Best-effort — a failure just delays persistence.</summary>
    public async Task SaveBlockStateAsync(string blockId, string stateJson, CancellationToken ct)
    {
        try
        {
            await _http.PutAsJsonAsync($"api/block-state/{Uri.EscapeDataString(blockId)}",
                new BlockStateRecord { Id = blockId, StateJson = stateJson }, Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist state for block {BlockId}", blockId);
        }
    }

    /// <summary>Drop a removed block's persisted state (Epic 2Q, Phase 2).</summary>
    public async Task DeleteBlockStateAsync(string blockId, CancellationToken ct)
    {
        try
        {
            await _http.DeleteAsync($"api/block-state/{Uri.EscapeDataString(blockId)}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete state for block {BlockId}", blockId);
        }
    }

    /// <summary>Current home mode (1G), or null if the gateway is unreachable.</summary>
    public async Task<string?> GetModeAsync(CancellationToken ct)
    {
        try
        {
            var state = await _http.GetFromJsonAsync<HomeStateDto>("api/mode", Json, ct);
            return state?.Mode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load home mode from DbGateway");
            return null;
        }
    }

    /// <summary>
    /// Request a home-mode switch (1G). The DbGateway persists it and publishes the change on the bus,
    /// which <see cref="HomeModeMonitor"/> then mirrors into <see cref="HomeModeState"/>. Idempotent on
    /// the gateway side, so a no-op switch is harmless. Returns false if the gateway is unreachable.
    /// </summary>
    public async Task<bool> SetModeAsync(string mode, string source, CancellationToken ct)
    {
        try
        {
            var response = await _http.PutAsJsonAsync("api/mode", new ModeUpdateDto(mode, source), Json, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set home mode {Mode} via DbGateway", mode);
            return false;
        }
    }

    private sealed record HomeStateDto(string Mode, string Source, DateTime UpdatedAt);
    private sealed record ModeUpdateDto(string Mode, string Source);

    /// <summary>
    /// Current site location (roadmap Epic 2K) for sunrise/sunset geometry. Null if the gateway is
    /// unreachable — the engine then keeps its last-known coordinates (offline-first).
    /// </summary>
    public async Task<Domovoy.Contracts.Home.SiteLocation?> GetLocationAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Home.SiteLocation>("api/settings/location", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load site location from DbGateway");
            return null;
        }
    }

    /// <summary>Tracked residents (roadmap Epic 3D) so the presence layer can project one person device each;
    /// null if the gateway is unreachable (the layer keeps its last-known roster, offline-first).</summary>
    public async Task<List<Domovoy.Contracts.Home.Resident>?> GetResidentsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<Domovoy.Contracts.Home.Resident>>("api/residents", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load residents from DbGateway");
            return null;
        }
    }

    /// <summary>Presence-layer settings (roadmap Epic 3D) — geofence radius + away-grace; null if unreachable.</summary>
    public async Task<Domovoy.Contracts.Home.PresenceSettings?> GetPresenceSettingsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Home.PresenceSettings>("api/settings/presence", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load presence settings from DbGateway");
            return null;
        }
    }

    /// <summary>Notification-discipline settings (roadmap Epic 3F) for the dispatcher — per-type routing, rate-limit,
    /// safety floor; null if unreachable (the dispatcher keeps its last-known settings, offline-first).</summary>
    public async Task<Domovoy.Contracts.Notifications.NotificationSettings?> GetNotificationSettingsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Notifications.NotificationSettings>("api/settings/notifications", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load notification settings from DbGateway");
            return null;
        }
    }

    /// <summary>Calendar settings (roadmap Epic 2L) for the Calendar sensor; null if the gateway is unreachable.</summary>
    public async Task<Domovoy.Contracts.Home.CalendarSettings?> GetCalendarSettingsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Home.CalendarSettings>("api/settings/calendar", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load calendar settings from DbGateway");
            return null;
        }
    }

    /// <summary>Tariff settings (roadmap Epic 3C) for the tariff device / cheap-hours block; null if unreachable.</summary>
    public async Task<Domovoy.Contracts.Home.TariffSettings?> GetTariffSettingsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Home.TariffSettings>("api/settings/tariff", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load tariff settings from DbGateway");
            return null;
        }
    }

    /// <summary>Load-management settings (roadmap Epic 3C-LM) for <see cref="LoadManager"/>; null if unreachable
    /// (the coordinator keeps its last-known config, offline-first).</summary>
    public async Task<Domovoy.Contracts.Home.LoadManagementSettings?> GetLoadManagementSettingsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Home.LoadManagementSettings>("api/settings/load-management", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load load-management settings from DbGateway");
            return null;
        }
    }

    /// <summary>Electrical topology (roadmap Epic 3C-D) — supplies/panels/circuits with phase + breaker
    /// rating, so LoadManager can budget per phase and per line. Null if unreachable (keep last-known).</summary>
    public async Task<List<PowerNodeSnapshot>?> GetPowerTopologyAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<PowerNodeSnapshot>>("api/power-topology", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the power topology from DbGateway");
            return null;
        }
    }

    public async Task<List<DeviceSnapshot>?> GetDevicesAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<DeviceSnapshot>>("api/capability-devices", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load device read-model from DbGateway");
            return null;
        }
    }

    // ===== Intelligence layer settings + journal (Epic 3I) =====

    /// <summary>Live ML-layer switches (Epic 3I); null if the gateway is unreachable (keep last-known, offline-first).</summary>
    public async Task<Domovoy.Contracts.Ml.MlSettings?> GetMlSettingsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<Domovoy.Contracts.Ml.MlSettings>("api/settings/ml", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load ML settings from DbGateway");
            return null;
        }
    }

    /// <summary>Append one entry to the ML-activity journal (Epic 3I). Best-effort — a failure just drops the entry.</summary>
    public async Task WriteMlActivityAsync(Domovoy.Contracts.Ml.MlActivityEntry entry, CancellationToken ct)
    {
        try
        {
            await _http.PostAsJsonAsync("api/ml/activity", entry, Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not write ML-activity entry ({Source})", entry.Source);
        }
    }

    /// <summary>
    /// Timestamp of the earliest recorded event (Epic 3I cold-start gate) — how old the history is. Null when the
    /// gateway is unreachable OR the log is empty (the caller treats both as "not mature yet").
    /// </summary>
    public async Task<DateTime?> GetEarliestEventAsync(CancellationToken ct)
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<EarliestDto>("api/events/earliest", Json, ct);
            return dto?.Earliest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load earliest event timestamp from DbGateway");
            return null;
        }
    }

    private sealed record EarliestDto(DateTime? Earliest);

    /// <summary>
    /// Fetch device-state deltas from the P0-5 event-log for replay/simulation (roadmap Epic 1F).
    /// Returns oldest-first; null on a gateway failure.
    /// </summary>
    public async Task<List<EventLogEntry>?> GetStateEventsAsync(DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct)
    {
        try
        {
            var url = $"api/events?kind=state_change&from={fromUtc:o}&to={toUtc:o}&limit={limit}";
            var events = await _http.GetFromJsonAsync<List<EventLogEntry>>(url, Json, ct);
            // The endpoint returns newest-first; replay needs chronological order.
            events?.Reverse();
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load event-log for replay from DbGateway");
            return null;
        }
    }

    /// <summary>
    /// State-change events for one capability (roadmap Epic 2I, Phase 1) — the label source for boolean/enum
    /// ML targets, mirroring how telemetry is the source for numeric ones. Oldest-first; optionally zone-scoped;
    /// null on a gateway failure.
    /// </summary>
    public async Task<List<EventLogEntry>?> GetCapabilityEventsAsync(
        string capabilityId, DateTime fromUtc, int limit, CancellationToken ct, string? zoneId = null, DateTime? toUtc = null)
    {
        try
        {
            var url = $"api/events?kind=state_change&capabilityId={Uri.EscapeDataString(capabilityId)}"
                + $"&from={fromUtc:o}&limit={limit}";
            if (toUtc is not null) url += $"&to={toUtc:o}";
            if (!string.IsNullOrEmpty(zoneId)) url += $"&zoneId={Uri.EscapeDataString(zoneId)}";
            var events = await _http.GetFromJsonAsync<List<EventLogEntry>>(url, Json, ct);
            events?.Reverse(); // endpoint returns newest-first; training needs chronological order
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load events for {Capability}", capabilityId);
            return null;
        }
    }

    /// <summary>
    /// Load the home-mode timeline (roadmap Epic 2B context-join): the <c>mode_change</c> records from the
    /// event-log (1G), chronological, as (time, mode) pairs. Used to attach the mode in effect at each training
    /// row. Null on a gateway failure; empty when no mode changes were recorded.
    /// </summary>
    public async Task<List<(DateTime At, string Mode)>?> GetModeTimelineAsync(DateTime fromUtc, int limit, CancellationToken ct)
    {
        try
        {
            var url = $"api/events?kind=mode_change&from={fromUtc:o}&limit={limit}";
            var events = await _http.GetFromJsonAsync<List<EventLogEntry>>(url, Json, ct);
            if (events is null) return null;
            events.Reverse(); // newest-first → chronological

            var timeline = new List<(DateTime, string)>();
            foreach (var e in events)
            {
                var mode = e.NewValue is { ValueKind: JsonValueKind.String } v ? v.GetString() : e.NewValue?.ToString();
                if (!string.IsNullOrWhiteSpace(mode)) timeline.Add((e.Timestamp, mode!));
            }
            return timeline;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load home-mode timeline from DbGateway");
            return null;
        }
    }

    // ===== Approval queue (Epic 2C) =====

    /// <summary>Create a candidate automation rule (Proposed) and return it with its server-assigned id, or null.</summary>
    public async Task<AutomationRule?> CreateRuleAsync(AutomationRule rule, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/automations", rule, Json, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<AutomationRule>(Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create candidate rule {Name}", rule.Name);
            return null;
        }
    }

    /// <summary>Queue a proposal for human approval (Epic 2C). Returns the stored proposal, or null on failure.</summary>
    public async Task<Proposal?> CreateProposalAsync(Proposal proposal, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/proposals", proposal, Json, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<Proposal>(Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create proposal {Title}", proposal.Title);
            return null;
        }
    }

    /// <summary>Existing proposals (optionally filtered by status), for de-duplication; null if unreachable.</summary>
    public async Task<List<Proposal>?> GetProposalsAsync(CancellationToken ct, string? status = null)
    {
        try
        {
            var url = string.IsNullOrEmpty(status) ? "api/proposals" : $"api/proposals?status={Uri.EscapeDataString(status)}";
            return await _http.GetFromJsonAsync<List<Proposal>>(url, Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load proposals from DbGateway");
            return null;
        }
    }

    // ===== ML substrate (Epic 2A) =====

    /// <summary>
    /// Numeric telemetry for training (Epic 2A). Optionally scoped to a zone (Epic 2I) so a zone- or
    /// zone-kind-scoped model trains only on its own readings. Null on failure.
    /// </summary>
    public async Task<List<TelemetrySample>?> GetTelemetryAsync(
        string capabilityId, DateTime fromUtc, int limit, CancellationToken ct, string? zoneId = null, DateTime? toUtc = null)
    {
        try
        {
            var url = $"api/telemetry?capabilityId={Uri.EscapeDataString(capabilityId)}&from={fromUtc:o}&limit={limit}";
            if (toUtc is not null) url += $"&to={toUtc:o}";
            if (!string.IsNullOrEmpty(zoneId)) url += $"&zoneId={Uri.EscapeDataString(zoneId)}";
            return await _http.GetFromJsonAsync<List<TelemetrySample>>(url, Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load telemetry for {Capability}", capabilityId);
            return null;
        }
    }

    /// <summary>Zones read-model (id, kind) for ML scope resolution (Epic 2I), or null if unreachable.</summary>
    public async Task<List<ZoneSnapshot>?> GetZonesAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ZoneSnapshot>>("api/zones", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load zones from DbGateway");
            return null;
        }
    }

    /// <summary>
    /// Register a freshly trained model (metadata + serialized artifact). <paramref name="keepLastVersions"/>
    /// &gt; 0 asks the gateway to prune the registered (kind, target, scope) line down to that many versions
    /// (Epic 2P retention). Returns the stored metadata.
    /// </summary>
    public async Task<MlModel?> RegisterModelAsync(MlModel model, byte[] artifact, int keepLastVersions, CancellationToken ct)
    {
        try
        {
            var body = new { model, artifactBase64 = Convert.ToBase64String(artifact) };
            var url = keepLastVersions > 0 ? $"api/ml/models?keepLast={keepLastVersions}" : "api/ml/models";
            var response = await _http.PostAsJsonAsync(url, body, Json, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<MlModel>(Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not register ML model {Name}", model.Name);
            return null;
        }
    }

    // ===== ML tasks (Epic 2P) =====

    /// <summary>All ML training tasks, or null if the gateway is unreachable.</summary>
    public async Task<List<MlTask>?> GetMlTasksAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MlTask>>("api/ml/tasks", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load ML tasks from DbGateway");
            return null;
        }
    }

    /// <summary>
    /// First-run seed of the options-derived default task (Epic 2P). Idempotent on the gateway side — it
    /// inserts only while the ml_tasks collection has never been written, so user deletions stick.
    /// </summary>
    public async Task<bool> SeedDefaultMlTaskAsync(MlTask task, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/ml/tasks/seed", task, Json, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not seed the default ML task");
            return false;
        }
    }

    /// <summary>Record the outcome of a training attempt on its task (trainer-owned status subdocument).</summary>
    public async Task UpdateMlTaskStatusAsync(string taskId, MlTaskStatus status, CancellationToken ct)
    {
        try
        {
            await _http.PutAsJsonAsync($"api/ml/tasks/{Uri.EscapeDataString(taskId)}/status", status, Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update ML task status for {TaskId}", taskId);
        }
    }

    /// <summary>Telemetry sample count in a window (Epic 2P data-sufficiency check), or null if unreachable.</summary>
    public async Task<long?> CountTelemetryAsync(string capabilityId, DateTime fromUtc, CancellationToken ct, string? zoneId = null)
    {
        var url = $"api/telemetry/count?capabilityId={Uri.EscapeDataString(capabilityId)}&from={fromUtc:o}";
        if (!string.IsNullOrEmpty(zoneId)) url += $"&zoneId={Uri.EscapeDataString(zoneId)}";
        return await CountAsync(url, ct);
    }

    /// <summary>Per-zone telemetry sample counts in a window (one aggregation round-trip), or null.</summary>
    public Task<IReadOnlyDictionary<string, long>?> CountTelemetryByZoneAsync(string capabilityId, DateTime fromUtc, CancellationToken ct) =>
        CountByZoneAsync($"api/telemetry/count-by-zone?capabilityId={Uri.EscapeDataString(capabilityId)}&from={fromUtc:o}", ct);

    /// <summary>State-change event count for one capability in a window, or null if unreachable.</summary>
    public async Task<long?> CountCapabilityEventsAsync(string capabilityId, DateTime fromUtc, CancellationToken ct, string? zoneId = null)
    {
        var url = $"api/events/count?capabilityId={Uri.EscapeDataString(capabilityId)}&from={fromUtc:o}";
        if (!string.IsNullOrEmpty(zoneId)) url += $"&zoneId={Uri.EscapeDataString(zoneId)}";
        return await CountAsync(url, ct);
    }

    /// <summary>Per-zone state-change event counts for one capability in a window, or null.</summary>
    public Task<IReadOnlyDictionary<string, long>?> CountCapabilityEventsByZoneAsync(string capabilityId, DateTime fromUtc, CancellationToken ct) =>
        CountByZoneAsync($"api/events/count-by-zone?capabilityId={Uri.EscapeDataString(capabilityId)}&from={fromUtc:o}", ct);

    private async Task<long?> CountAsync(string url, CancellationToken ct)
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<CountDto>(url, Json, ct);
            return dto?.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load count from {Url}", url);
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, long>?> CountByZoneAsync(string url, CancellationToken ct)
    {
        try
        {
            var rows = await _http.GetFromJsonAsync<List<ZoneCountDto>>(url, Json, ct);
            return rows?.ToDictionary(r => r.ZoneId, r => r.Count, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load per-zone counts from {Url}", url);
            return null;
        }
    }

    private sealed record CountDto(long Count);
    private sealed record ZoneCountDto(string ZoneId, long Count);

    /// <summary>All registered model metadata (artifact projected out), newest first, or null (Epic 2I).</summary>
    public async Task<List<MlModel>?> GetModelsAsync(CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MlModel>>("api/ml/models", Json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load ML model list");
            return null;
        }
    }

    /// <summary>Latest registered model metadata for a (kind, target, scope), or null (Epic 2A/2I).</summary>
    public async Task<MlModel?> GetLatestModelAsync(string kind, string target, ModelScope scope, CancellationToken ct)
    {
        try
        {
            var url = $"api/ml/models/latest?kind={Uri.EscapeDataString(kind)}&target={Uri.EscapeDataString(target)}"
                + $"&level={Uri.EscapeDataString(scope.Level)}&key={Uri.EscapeDataString(scope.Key)}";
            var response = await _http.GetAsync(url, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<MlModel>(Json, ct) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load latest ML model");
            return null;
        }
    }

    /// <summary>Download a model's serialized artifact, or null.</summary>
    public async Task<byte[]?> GetModelArtifactAsync(string id, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetAsync($"api/ml/models/{Uri.EscapeDataString(id)}/artifact", ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not download ML artifact {Id}", id);
            return null;
        }
    }

    /// <summary>One numeric telemetry sample (mirrors DbGateway TelemetryDto).</summary>
    public sealed class TelemetrySample
    {
        public DateTime Timestamp { get; set; }
        public string CapabilityId { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    /// <summary>Subset of the zones read-model for ML scope resolution (Epic 2I): id + kind.</summary>
    public sealed class ZoneSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string? Kind { get; set; }
    }

    /// <summary>Subset of the capability-device read-model the engine needs (zone + state + archetype/caps, 2D).</summary>
    public sealed class DeviceSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> State { get; set; } = new();

        /// <summary>Reachability from the read-model — the energy estimator pauses on an offline device
        /// (unknown state is not the same as zero consumption).</summary>
        public bool IsOnline { get; set; }

        // Epic 2D: semantic archetype + capability signature (for archetype-aware proposals + ML classifier).
        public List<CapabilitySnapshot> Capabilities { get; set; } = new();
        public string AutoArchetype { get; set; } = Contracts.Devices.DeviceArchetypes.Unknown;
        public string? Archetype { get; set; }

        /// <summary>User override wins over the auto-inferred archetype (mirrors the WebUI/read-model rule).</summary>
        [JsonIgnore]
        public string EffectiveArchetype => string.IsNullOrWhiteSpace(Archetype) ? AutoArchetype : Archetype!;

        /// <summary>Epic 3C-D energy profile — accounting toggle, role and nameplate watts; null ⇒ defaults.</summary>
        public EnergyProfileSnapshot? EnergyProfile { get; set; }

        /// <summary>Epic 3C-LM load-shedding profile; null ⇒ the device is unmanaged by LoadManager.</summary>
        public LoadSheddingProfileSnapshot? LoadShedding { get; set; }

        /// <summary>True when the device carries a cumulative <c>energy</c> series (its adapter's or the
        /// platform's synthetic one) — the accounting default for <see cref="TracksEnergy"/>.</summary>
        [JsonIgnore]
        public bool IsMetered => Capabilities.Any(c => c.Id == CapabilityIds.Energy);

        /// <summary>Whether this device counts toward energy totals: the explicit toggle, else metered-by-default
        /// (mirrors DbGateway <c>EnergyEndpoints.CountsTowardTotals</c>).</summary>
        [JsonIgnore]
        public bool TracksEnergy => EnergyProfile?.Track ?? IsMetered;
    }

    /// <summary>One node of the electrical topology (Epic 3C-D) — mirrors DbGateway's <c>PowerNode</c>.</summary>
    public sealed class PowerNodeSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string? Phase { get; set; }
        public double? BreakerAmps { get; set; }
        public double? Voltage { get; set; }
        public string? MeterDeviceId { get; set; }
    }

    /// <summary>Per-device energy accounting (Epic 3C-D) — mirrors DbGateway's <c>EnergyProfile</c> document
    /// shape (not shared: the read-model duplicates DTOs by convention, see <see cref="CapabilitySnapshot"/>).</summary>
    public sealed class EnergyProfileSnapshot
    {
        public bool? Track { get; set; }
        public string? Role { get; set; }
        public double? MaxPowerW { get; set; }
        public double? MinPowerW { get; set; }
        public double? StandbyPowerW { get; set; }
        public string? ScaleCapabilityId { get; set; }
        public string? CircuitId { get; set; }
    }

    /// <summary>Per-device load-shedding configuration (Epic 3C-LM) — mirrors DbGateway's
    /// <c>LoadSheddingProfile</c> document shape (not shared: the read-model duplicates DTOs by convention,
    /// see <see cref="CapabilitySnapshot"/>).</summary>
    public sealed class LoadSheddingProfileSnapshot
    {
        public bool Enabled { get; set; }
        public bool Protected { get; set; }
        public string ControlCapabilityId { get; set; } = "on_off";
        public bool Curtailable { get; set; }
        public double? CurtailedValue { get; set; }
        public double? RestoreValue { get; set; }
        public Dictionary<string, string> ModeTier { get; set; } = new();
        public Dictionary<string, int> ModePriority { get; set; } = new();
    }

    /// <summary>One capability of a device (id + writability) from the read-model.</summary>
    public sealed class CapabilitySnapshot
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Value type the adapter declared (<c>Boolean</c>/<c>Number</c>/<c>Enum</c>/…) — the
        /// descriptor the ML trainer types its target from (Epic 2I, <see cref="Ml.Templates.CapabilityKindResolver"/>).</summary>
        public string Kind { get; set; } = string.Empty;

        public bool Writable { get; set; }

        /// <summary>True for a series the platform maintains rather than the adapter reporting it (Epic 3C-D)
        /// — the estimator must not treat its own output as a measurement.</summary>
        public bool Synthetic { get; set; }
    }

    /// <summary>One event-log delta as served by <c>GET /api/events</c> (mirrors DbGateway EventLogDto).</summary>
    public sealed class EventLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string CapabilityId { get; set; } = string.Empty;
        public JsonElement? OldValue { get; set; }
        public JsonElement? NewValue { get; set; }
        public string TriggerSource { get; set; } = string.Empty;

        /// <summary>Concrete initiator id behind <see cref="TriggerSource"/> — e.g. the scene id for a
        /// <c>scene</c>-sourced change (Epic 2F scene-schedule discovery keys off this).</summary>
        public string? TriggerId { get; set; }

        public string? Mode { get; set; }
    }
}
