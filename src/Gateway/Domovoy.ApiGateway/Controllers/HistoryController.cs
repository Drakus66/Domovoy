using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy over the feature-store read endpoints (roadmap P0-5): the domain event-log
/// (<c>/api/events</c>) and numeric telemetry (<c>/api/telemetry</c>) served by the DbGateway.
/// Mirrors <see cref="CapabilityDevicesController"/>; the full query string is forwarded as-is.
/// </summary>
[ApiController]
public class HistoryController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HistoryController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <summary>Device event-log history (state deltas + commands) with trigger attribution.</summary>
    [HttpGet("api/events")]
    public Task<IActionResult> Events(CancellationToken ct) => Forward("api/events", ct);

    /// <summary>Numeric telemetry samples over a period.</summary>
    [HttpGet("api/telemetry")]
    public Task<IActionResult> Telemetry(CancellationToken ct) => Forward("api/telemetry", ct);

    private async Task<IActionResult> Forward(string path, CancellationToken ct)
    {
        var relativePath = Request.QueryString.HasValue ? $"{path}{Request.QueryString.Value}" : path;
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var upstream = await client.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
