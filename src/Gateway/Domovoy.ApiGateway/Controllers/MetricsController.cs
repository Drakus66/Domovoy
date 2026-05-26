using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(IHttpClientFactory httpClientFactory, ILogger<MetricsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("services")]
    public async Task<IActionResult> GetServicesStatus(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("prometheus");
            var response = await client.GetFromJsonAsync<PrometheusQueryResponse>(
                "/api/v1/query?query=up", cancellationToken);

            if (response?.Status != "success")
                return StatusCode(502, new { error = "Prometheus returned non-success status" });

            var services = response.Data.Result.Select(r => new ServiceStatusDto(
                Name: r.Metric.GetValueOrDefault("job", "unknown"),
                Instance: r.Metric.GetValueOrDefault("instance", "unknown"),
                IsUp: r.Value.Length > 1 && r.Value[1].ToString() == "1"
            )).ToList();

            return Ok(services);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Prometheus");
            return StatusCode(503, new { error = "Prometheus unreachable" });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("prometheus");

            var queries = new Dictionary<string, string>
            {
                ["servicesUp"]     = "count(up == 1)",
                ["servicesTotal"]  = "count(up)",
                ["mqttConnections"] = "rabbitmq_connections",
                ["apiRequestRate"] = "sum(rate(http_requests_received_total[5m]))",
                ["memoryBytes"]    = "sum(dotnet_total_memory_bytes)",
            };

            var results = await Task.WhenAll(queries.Select(async kvp =>
            {
                try
                {
                    var resp = await client.GetFromJsonAsync<PrometheusQueryResponse>(
                        $"/api/v1/query?query={Uri.EscapeDataString(kvp.Value)}", cancellationToken);

                    var raw = resp?.Data?.Result?.FirstOrDefault()?.Value;
                    var value = raw?.Length > 1 ? raw[1]?.ToString() : null;
                    return (kvp.Key, Value: value);
                }
                catch
                {
                    return (kvp.Key, Value: (string?)null);
                }
            }));

            var dict = results.ToDictionary(r => r.Key, r => r.Value);

            return Ok(new
            {
                servicesUp     = ParseInt(dict, "servicesUp"),
                servicesTotal  = ParseInt(dict, "servicesTotal"),
                mqttConnections = ParseInt(dict, "mqttConnections"),
                apiRequestRate = ParseDouble(dict, "apiRequestRate"),
                memoryMb       = ParseDouble(dict, "memoryBytes") is double mb ? Math.Round(mb / 1024 / 1024, 1) : (double?)null,
                collectedAt    = DateTime.UtcNow,
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Prometheus");
            return StatusCode(503, new { error = "Prometheus unreachable" });
        }
    }

    private static int? ParseInt(Dictionary<string, string?> d, string key)
        => d.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : null;

    private static double? ParseDouble(Dictionary<string, string?> d, string key)
        => d.TryGetValue(key, out var v) && double.TryParse(v,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
}

internal record ServiceStatusDto(string Name, string Instance, bool IsUp);

internal class PrometheusQueryResponse
{
    public string Status { get; set; } = string.Empty;
    public PrometheusData Data { get; set; } = new();
}

internal class PrometheusData
{
    public string ResultType { get; set; } = string.Empty;
    public List<PrometheusResult> Result { get; set; } = [];
}

internal class PrometheusResult
{
    public Dictionary<string, string> Metric { get; set; } = [];
    public object[] Value { get; set; } = [];
}
