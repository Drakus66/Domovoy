// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the home mode / presence context (roadmap Epic 1G), mirroring
/// <see cref="ZonesController"/>. Forwards reads and the manual switch to the DbGateway
/// <c>/api/mode</c> endpoints, which persist the mode and publish the change on the bus.
/// </summary>
[ApiController]
[Route("api/mode")]
public class ModeController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModeController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/mode", ct);

    [HttpGet("options")]
    public Task<IActionResult> Options(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/mode/options", ct);

    [HttpPut]
    public Task<IActionResult> Set(CancellationToken ct)
        => Forward(HttpMethod.Put, "api/mode", ct);

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
