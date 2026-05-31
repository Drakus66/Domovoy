using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for automation rule management + run history (roadmap Epic 1A). Forwards CRUD,
/// status toggles and history queries to the DbGateway <c>/api/automations</c> endpoints, mirroring
/// <see cref="ZonesController"/>. The AutomationService loads rules directly from the DbGateway; this
/// proxy is for the WebUI.
/// </summary>
[ApiController]
[Route("api/automations")]
public class AutomationsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AutomationsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/automations", ct);

    [HttpGet("history")]
    public Task<IActionResult> History(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/automations/history", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/automations/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/automations", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/automations/{Uri.EscapeDataString(id)}", ct);

    [HttpPut("{id}/status")]
    public Task<IActionResult> SetStatus(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/automations/{Uri.EscapeDataString(id)}/status", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/automations/{Uri.EscapeDataString(id)}", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var relativePath = Request.QueryString.HasValue ? $"{path}{Request.QueryString.Value}" : path;
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var request = new HttpRequestMessage(method, relativePath);

        if (method == HttpMethod.Post || method == HttpMethod.Put)
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var responseBody = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(responseBody) ? null : responseBody,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
