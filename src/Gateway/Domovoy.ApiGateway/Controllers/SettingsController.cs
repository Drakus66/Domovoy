// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for installation settings (roadmap Epic 2K). Forwards the site-location document,
/// offline timezone lookup and the optional geocode helpers to the DbGateway settings endpoints
/// (<c>/api/settings/*</c>), mirroring <see cref="RolesController"/>. The query string is relayed verbatim
/// so the coordinate/geocode GETs keep their parameters.
/// </summary>
[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SettingsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet("location")]
    public Task<IActionResult> GetLocation(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/settings/location", ct);

    [HttpPut("location")]
    public Task<IActionResult> PutLocation(CancellationToken ct)
        => Forward(HttpMethod.Put, "api/settings/location", ct);

    [HttpGet("timezone")]
    public Task<IActionResult> Timezone(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/settings/timezone" + Request.QueryString.Value, ct);

    [HttpGet("geocode")]
    public Task<IActionResult> Geocode(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/settings/geocode" + Request.QueryString.Value, ct);

    [HttpGet("reverse-geocode")]
    public Task<IActionResult> ReverseGeocode(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/settings/reverse-geocode" + Request.QueryString.Value, ct);

    [HttpGet("calendar")]
    public Task<IActionResult> GetCalendar(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/settings/calendar", ct);

    [HttpPut("calendar")]
    public Task<IActionResult> PutCalendar(CancellationToken ct)
        => Forward(HttpMethod.Put, "api/settings/calendar", ct);

    [HttpPost("calendar/import")]
    public Task<IActionResult> ImportHolidays(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/settings/calendar/import" + Request.QueryString.Value, ct);

    private async Task<IActionResult> Forward(HttpMethod method, string relativePath, CancellationToken ct)
    {
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
