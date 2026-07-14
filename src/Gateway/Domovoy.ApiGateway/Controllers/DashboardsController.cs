// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for custom dashboard tabs (custom dashboards epic). Forwards CRUD, tab
/// ordering and the hidden-spheres preference to the DbGateway dashboard endpoints
/// (<c>/api/dashboards</c>) over the in-cluster <c>db-gateway</c> HttpClient, mirroring
/// <see cref="ZonesController"/>. Dashboards are pure presentation state (no bus involvement).
/// </summary>
[ApiController]
[Route("api/dashboards")]
public class DashboardsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DashboardsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/dashboards", ct);

    [HttpGet("prefs")]
    public Task<IActionResult> GetPrefs(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/dashboards/prefs", ct);

    [HttpPut("prefs")]
    public Task<IActionResult> UpdatePrefs(CancellationToken ct)
        => Forward(HttpMethod.Put, "api/dashboards/prefs", ct);

    [HttpPut("order")]
    public Task<IActionResult> Reorder(CancellationToken ct)
        => Forward(HttpMethod.Put, "api/dashboards/order", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/dashboards/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/dashboards", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/dashboards/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/dashboards/{Uri.EscapeDataString(id)}", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string relativePath, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var request = new HttpRequestMessage(method, relativePath);

        // Relay the original JSON body for writes (create/update/order/prefs).
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
