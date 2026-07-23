// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy over the Epic 3C energy-accounting read endpoints served by the DbGateway
/// (<c>/api/energy/*</c>). Mirrors <see cref="HistoryController"/>; the full query string is forwarded as-is.
/// </summary>
[ApiController]
public class EnergyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EnergyController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <summary>Per-device energy consumption (kWh) + honest totals for a window (roadmap Epic 3C).</summary>
    [HttpGet("api/energy/consumption")]
    public Task<IActionResult> Consumption(CancellationToken ct) => Forward("api/energy/consumption", ct);

    /// <summary>Household energy cost for a window, broken down by tariff zone (roadmap Epic 3C).</summary>
    [HttpGet("api/energy/cost")]
    public Task<IActionResult> Cost(CancellationToken ct) => Forward("api/energy/cost", ct);

    /// <summary>Consumption/draw per circuit and per phase, with the balance check (roadmap Epic 3C-D).</summary>
    [HttpGet("api/energy/breakdown")]
    public Task<IActionResult> Breakdown(CancellationToken ct) => Forward("api/energy/breakdown", ct);

    private async Task<IActionResult> Forward(string path, CancellationToken ct)
    {
        var relativePath = Request.QueryString.HasValue ? $"{path}{Request.QueryString.Value}" : path;
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var upstream = await client.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
