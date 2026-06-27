using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the ML substrate (roadmap Epic 2A). The model registry (list) lives in the
/// DbGateway; training is triggered on the AutomationService (which owns the trainer + model loader).
/// </summary>
[ApiController]
[Route("api/ml")]
public class MlController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MlController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <summary>List registered model metadata (newest first).</summary>
    [HttpGet("models")]
    public Task<IActionResult> Models(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/ml/models", ct);

    /// <summary>Trigger a training run now.</summary>
    [HttpPost("train")]
    public Task<IActionResult> Train(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Post, "api/ml/train", ct);

    /// <summary>Backtest scorecard (Epic 2B): the loaded model's prediction vs actual telemetry.</summary>
    [HttpGet("backtest")]
    public Task<IActionResult> Backtest([FromQuery] int days, CancellationToken ct)
        => Forward("automation-service", HttpMethod.Get, $"api/ml/backtest?days={(days > 0 ? days : 7)}", ct);

    private async Task<IActionResult> Forward(string client, HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient(client);
        using var request = new HttpRequestMessage(method, path);
        using var upstream = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(body) ? null : body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
