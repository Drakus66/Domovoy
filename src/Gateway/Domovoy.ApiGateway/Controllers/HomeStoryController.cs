// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the House Diary (roadmap Epic 2N): the materialized diary feed, on-the-fly preview,
/// manual rebuild, and the personalization overrides — all served by the DbGateway. Forwards over the
/// in-cluster <c>db-gateway</c> HttpClient, mirroring <see cref="HistoryController"/> / <see cref="UsersController"/>.
/// </summary>
[ApiController]
public class HomeStoryController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HomeStoryController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet("api/home-story")]
    public Task<IActionResult> Feed(CancellationToken ct) => Forward(HttpMethod.Get, "api/home-story", ct);

    [HttpPost("api/home-story/preview")]
    public Task<IActionResult> Preview(CancellationToken ct) => Forward(HttpMethod.Post, "api/home-story/preview", ct);

    [HttpPost("api/home-story/rebuild")]
    public Task<IActionResult> Rebuild(CancellationToken ct) => Forward(HttpMethod.Post, "api/home-story/rebuild", ct);

    [HttpGet("api/narrative-entities")]
    public Task<IActionResult> Entities(CancellationToken ct) => Forward(HttpMethod.Get, "api/narrative-entities", ct);

    [HttpPost("api/narrative-entities")]
    public Task<IActionResult> CreateEntity(CancellationToken ct) => Forward(HttpMethod.Post, "api/narrative-entities", ct);

    [HttpPut("api/narrative-entities/{id}")]
    public Task<IActionResult> UpdateEntity(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/narrative-entities/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("api/narrative-entities/{id}")]
    public Task<IActionResult> DeleteEntity(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/narrative-entities/{Uri.EscapeDataString(id)}", ct);

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
