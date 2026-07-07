// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for notification delivery channels (roadmap Epic 2G). Forwards to the
/// AutomationService, which hosts the channels + dispatcher (they run alongside rule execution). Lets the
/// UI show which channels are enabled and send a test message. Mirrors <see cref="AssistantController"/>.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NotificationsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet("channels")]
    public Task<IActionResult> Channels(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/notifications/channels", ct);

    [HttpPost("test")]
    public Task<IActionResult> Test(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/notifications/test", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("automation-service");
        using var request = new HttpRequestMessage(method, path);

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
