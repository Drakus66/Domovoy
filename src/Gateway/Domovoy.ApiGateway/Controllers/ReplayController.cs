// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for rule replay/simulation (roadmap Epic 1F). Forwards a candidate rule + window
/// to the AutomationService <c>/api/replay</c> (which owns the rule evaluator), so the WebUI can dry-run
/// a rule over history — "when would this have fired?" — before activating it.
/// </summary>
[ApiController]
[Route("api/replay")]
public class ReplayController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ReplayController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpPost]
    public async Task<IActionResult> Replay(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("automation-service");

        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/replay")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

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
