// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text;

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy over the feature-store read endpoints (roadmap P0-5): the domain event-log
/// (<c>/api/events</c>) and numeric telemetry (<c>/api/telemetry</c>) served by the DbGateway.
/// Mirrors <see cref="CapabilityDevicesController"/>; the full query string is forwarded as-is.
/// </summary>
[ApiController]
public class HistoryController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HistoryController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <summary>Device event-log history (state deltas + commands) with trigger attribution.</summary>
    [HttpGet("api/events")]
    public Task<IActionResult> Events(CancellationToken ct) => Forward("api/events", ct);

    /// <summary>Unified activity feed: device events + automation runs + ops logs (roadmap Epic 2G).</summary>
    [HttpGet("api/activity")]
    public Task<IActionResult> Activity(CancellationToken ct) => Forward("api/activity", ct);

    /// <summary>Numeric telemetry samples over a period (supports <c>?format=csv</c> export).</summary>
    [HttpGet("api/telemetry")]
    public Task<IActionResult> Telemetry(CancellationToken ct) => Forward("api/telemetry", ct);

    /// <summary>Aggregated telemetry rollups (minute/hour/day) for trend charts (roadmap Epic 1B).</summary>
    [HttpGet("api/telemetry/aggregate")]
    public Task<IActionResult> TelemetryAggregate(CancellationToken ct) => Forward("api/telemetry/aggregate", ct);

    /// <summary>Many (device, capability) telemetry series in one round-trip (dashboard sparklines / composed charts).</summary>
    [HttpPost("api/telemetry/aggregate/batch")]
    public Task<IActionResult> TelemetryAggregateBatch(CancellationToken ct) => ForwardPost("api/telemetry/aggregate/batch", ct);

    /// <summary>Latest event-log row per device in one round-trip (per-tile "last changed by …" provenance).</summary>
    [HttpPost("api/events/latest-by-device")]
    public Task<IActionResult> EventsLatestByDevice(CancellationToken ct) => ForwardPost("api/events/latest-by-device", ct);

    private async Task<IActionResult> Forward(string path, CancellationToken ct)
    {
        var relativePath = Request.QueryString.HasValue ? $"{path}{Request.QueryString.Value}" : path;
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var upstream = await client.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, ct);
        return await Relay(upstream, ct);
    }

    private async Task<IActionResult> ForwardPost(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var upstream = await client.PostAsync(path, content, ct);
        return await Relay(upstream, ct);
    }

    private static async Task<IActionResult> Relay(HttpResponseMessage upstream, CancellationToken ct)
    {
        var body = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = body,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
