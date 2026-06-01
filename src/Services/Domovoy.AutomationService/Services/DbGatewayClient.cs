using System.Net.Http.Json;
using System.Text.Json;

using Domovoy.Contracts.Automations;
using Domovoy.Contracts.Blocks;

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

    /// <summary>Subset of the capability-device read-model the engine needs (zone + current state).</summary>
    public sealed class DeviceSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> State { get; set; } = new();
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
        public string? Mode { get; set; }
    }
}
