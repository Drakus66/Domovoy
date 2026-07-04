using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the integration-plugin registry + lifecycle (roadmap Epic 1C). Forwards to the
/// PluginSupervisor, which owns manifest discovery, resource-aware gating and process supervision.
/// </summary>
[ApiController]
[Route("api/plugins")]
public class PluginsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PluginsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/plugins", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/plugins/{Uri.EscapeDataString(id)}", ct);

    [HttpPost("{id}/start")]
    public Task<IActionResult> Start(string id, CancellationToken ct)
        => Forward(HttpMethod.Post, $"api/plugins/{Uri.EscapeDataString(id)}/start", ct);

    [HttpPost("{id}/stop")]
    public Task<IActionResult> Stop(string id, CancellationToken ct)
        => Forward(HttpMethod.Post, $"api/plugins/{Uri.EscapeDataString(id)}/stop", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("plugin-supervisor");
        using var request = new HttpRequestMessage(method, path);
        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(body) ? null : body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
