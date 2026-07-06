using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the natural-language assistant extension point (roadmap Epic 2H). Forwards to the
/// AutomationService, which hosts the connector (it owns rule authoring + 1F attribution). The capability is a
/// stub gated by a feature flag: while disabled the upstream returns a graceful "not configured" result, so the
/// UI degrades cleanly. Mirrors <see cref="ProposalsController"/> in fronting the AutomationService.
/// </summary>
[ApiController]
[Route("api/assistant")]
public class AssistantController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AssistantController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/assistant/status", ct);

    [HttpPost("author-rule")]
    public Task<IActionResult> AuthorRule(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/assistant/author-rule", ct);

    [HttpPost("explain")]
    public Task<IActionResult> Explain(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/assistant/explain", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("automation-service");
        using var request = new HttpRequestMessage(method, path);

        if (method == HttpMethod.Post || method == HttpMethod.Put)
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);
            if (!string.IsNullOrEmpty(body))
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var upstream = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var responseBody = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(responseBody) ? null : responseBody,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
