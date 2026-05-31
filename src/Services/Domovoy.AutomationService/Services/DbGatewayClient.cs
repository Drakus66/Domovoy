using System.Net.Http.Json;
using System.Text.Json;

using Domovoy.Contracts.Automations;

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

    /// <summary>Subset of the capability-device read-model the engine needs (zone + current state).</summary>
    public sealed class DeviceSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> State { get; set; } = new();
    }
}
