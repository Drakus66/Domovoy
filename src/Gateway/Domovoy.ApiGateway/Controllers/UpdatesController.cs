// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Delivery and updates (roadmap Epic 3K). A thin reverse proxy over two upstreams: the settings live
/// with the rest of the domain state on the DbGateway, while the registry, planning and execution are
/// the delivery service's job.
/// <para>
/// Gated on <c>system.admin</c> — pressing this replaces running software on the house — and it is the
/// only route to the delivery service, which publishes no ports outside <c>domovoy-network</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/updates")]
[Authorize(Policy = WellKnownPermissions.SystemAdmin)]
public class UpdatesController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public UpdatesController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    // ---- настройки (db-gateway) ----

    [HttpGet("settings")]
    public Task<IActionResult> GetSettings(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Get, "api/update-settings", ct);

    [HttpPut("settings")]
    public Task<IActionResult> PutSettings(CancellationToken ct)
        => Forward("db-gateway", HttpMethod.Put, "api/update-settings", ct, forwardBody: true);

    // ---- состав и планирование (служба обновлений) ----

    /// <summary>Installed versus available, per component — the table the settings page renders.</summary>
    [HttpGet("components")]
    public Task<IActionResult> Components(CancellationToken ct)
        => Forward("updater", HttpMethod.Get, "api/components", ct);

    [HttpGet("check")]
    public Task<IActionResult> Check(CancellationToken ct)
        => Forward("updater", HttpMethod.Get, "api/updates/check", ct);

    /// <summary>
    /// What updating these components would actually do — including what gets pulled in and why.
    /// Deliberately a separate call from <see cref="Apply"/>: the confirmation dialog has to show the
    /// consequences before anything happens.
    /// </summary>
    [HttpPost("plan")]
    public Task<IActionResult> Plan(CancellationToken ct)
        => Forward("updater", HttpMethod.Post, "api/updates/plan", ct, forwardBody: true);

    [HttpPost("apply")]
    public Task<IActionResult> Apply(CancellationToken ct)
        => Forward("updater", HttpMethod.Post, "api/updates/apply", ct, forwardBody: true);

    /// <summary>
    /// Progress of the current run. Polled rather than pushed: an update recreates this very gateway
    /// and the WebUI near the end, so the connection is expected to drop — the state lives in a file
    /// on the host and the browser picks the story back up on reconnect.
    /// </summary>
    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct)
        => Forward("updater", HttpMethod.Get, "api/updates/status", ct);

    [HttpGet("history")]
    public Task<IActionResult> History(CancellationToken ct)
        => Forward("updater", HttpMethod.Get, "api/updates/history", ct);

    [HttpPost("rollback")]
    public Task<IActionResult> Rollback(CancellationToken ct)
        => Forward("updater", HttpMethod.Post, "api/updates/rollback", ct);

    private async Task<IActionResult> Forward(
        string upstream, HttpMethod method, string relativePath, CancellationToken ct, bool forwardBody = false)
    {
        var client = _httpClientFactory.CreateClient(upstream);
        using var request = new HttpRequestMessage(method, relativePath);

        if (forwardBody)
        {
            request.Content = new StreamContent(Request.Body);
            if (!string.IsNullOrEmpty(Request.ContentType))
                request.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = string.IsNullOrEmpty(body) ? null : body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
            };
        }
        catch (HttpRequestException)
        {
            // Служба обновлений опциональна: стек, поднятый без неё, должен вести себя внятно,
            // а не отдавать 500 на страницу настроек.
            return new ContentResult
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                Content = """{"error":"Служба обновлений недоступна"}""",
                ContentType = "application/json",
            };
        }
    }
}
