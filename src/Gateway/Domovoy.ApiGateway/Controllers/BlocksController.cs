using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for control blocks (roadmap Epic 1H). CRUD is forwarded to the DbGateway
/// <c>/api/blocks</c> (persistence), while the type <c>catalog</c> comes from the AutomationService
/// (which owns the block runtime + registered types). Mirrors <see cref="AutomationsController"/>.
/// </summary>
[ApiController]
[Route("api/blocks")]
public class BlocksController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BlocksController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    // The type catalog lives in the AutomationService. Declared before {id} so "catalog" isn't taken as an id.
    [HttpGet("catalog")]
    public Task<IActionResult> Catalog(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Get, "api/blocks/catalog", ct);

    // Runtime health (last-tick/error per block) also lives in the AutomationService (owns the runtime).
    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct)
        => Forward("automation-service", HttpMethod.Get, "api/blocks/status", ct);

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/blocks", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, $"api/blocks/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Post, "api/blocks", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Put, $"api/blocks/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Delete, $"api/blocks/{Uri.EscapeDataString(id)}", ct);

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
