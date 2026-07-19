// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the integration-plugin registry + lifecycle (roadmap Epic 1C). Forwards to the
/// PluginSupervisor, which owns manifest discovery, resource-aware gating and process supervision.
/// Gated on <c>plugins.manage</c> — installing/starting plugins is a privileged surface.
/// </summary>
[ApiController]
[Route("api/plugins")]
[Authorize(Policy = WellKnownPermissions.PluginsManage)]
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

    /// <summary>
    /// Upload a plugin package (zip). The browser posts it as a multipart form field <c>package</c>; we parse
    /// the file here (the gateway is the right place for multipart) and forward the raw zip bytes to the
    /// supervisor so it never has to re-parse a proxied multipart stream.
    /// </summary>
    [HttpPost("install")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Install([FromForm(Name = "package")] IFormFile? package, CancellationToken ct)
    {
        if (package is null || package.Length == 0)
            return BadRequest(new { result = "no file field 'package' in the upload" });

        var client = _httpClientFactory.CreateClient("plugin-supervisor");
        await using var stream = package.OpenReadStream();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/plugins/install")
        {
            Content = new StreamContent(stream),
        };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/zip");

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(body) ? null : body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }

    [HttpDelete("{id}")]
    public Task<IActionResult> Uninstall(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/plugins/{Uri.EscapeDataString(id)}", ct);

    /// <summary>Schema + current values of a plugin's settings (secrets masked by the supervisor).</summary>
    [HttpGet("{id}/settings")]
    public Task<IActionResult> GetSettings(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/plugins/{Uri.EscapeDataString(id)}/settings", ct);

    /// <summary>Save settings; the supervisor persists them and broadcasts them to the plugin (applied live).</summary>
    [HttpPut("{id}/settings")]
    public Task<IActionResult> UpdateSettings(string id, [FromBody] object body, CancellationToken ct)
        => ForwardJson(HttpMethod.Put, $"api/plugins/{Uri.EscapeDataString(id)}/settings", body, ct);

    private async Task<IActionResult> ForwardJson(HttpMethod method, string path, object body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("plugin-supervisor");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var respBody = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(respBody) ? null : respBody,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }

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
