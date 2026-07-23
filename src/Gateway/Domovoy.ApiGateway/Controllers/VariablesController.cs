// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for global-variable management (roadmap Epic 3E, Hubitat "Hub Variables"). Forwards
/// CRUD to the DbGateway <c>/api/variables</c> endpoints, mirroring <see cref="AutomationsController"/>.
/// The AutomationService reads variables directly from the DbGateway; this proxy is for the WebUI.
/// </summary>
[ApiController]
[Route("api/variables")]
public class VariablesController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public VariablesController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/variables", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/variables/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/variables", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/variables/{Uri.EscapeDataString(id)}", ct);

    [HttpPut("{id}/value")]
    public Task<IActionResult> SetValue(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/variables/{Uri.EscapeDataString(id)}/value", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/variables/{Uri.EscapeDataString(id)}", ct);

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
