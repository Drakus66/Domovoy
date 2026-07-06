using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the approval queue (roadmap Epic 2C). The queue itself (list/create/approve/reject)
/// lives in the DbGateway, which owns the target collections and applies the side-effects; the heuristic
/// proposer's manual scan runs on the AutomationService (which reads the event-log and owns the blocks).
/// Mirrors <see cref="MlController"/> in fronting two upstreams behind one route.
/// </summary>
[ApiController]
[Route("api/proposals")]
public class ProposalsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ProposalsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/proposals", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, $"api/proposals/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Post, "api/proposals", ct);

    [HttpPost("{id}/approve")]
    public Task<IActionResult> Approve(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Post, $"api/proposals/{Uri.EscapeDataString(id)}/approve", ct);

    [HttpPost("{id}/reject")]
    public Task<IActionResult> Reject(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Post, $"api/proposals/{Uri.EscapeDataString(id)}/reject", ct);

    /// <summary>Run the heuristic proposer now (Epic 2C stub-precursor to 2F); mines the event-log for candidates.</summary>
    [HttpPost("suggest")]
    public Task<IActionResult> Suggest(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Post, "api/proposals/suggest", ct);

    /// <summary>Run the pattern-discovery engine now (Epic 2F); the full MI/FDR funnel over history → queued proposals.</summary>
    [HttpPost("discover")]
    public Task<IActionResult> Discover(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Post, "api/discovery/scan", ct);

    private async Task<IActionResult> Forward(string client, HttpMethod method, string path, CancellationToken ct)
    {
        var relativePath = Request.QueryString.HasValue ? $"{path}{Request.QueryString.Value}" : path;
        var http = _httpClientFactory.CreateClient(client);
        using var request = new HttpRequestMessage(method, relativePath);

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
