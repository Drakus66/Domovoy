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
public class HistoryController : ProxyController
{
    public HistoryController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    /// <summary>Device event-log history (state deltas + commands) with trigger attribution.</summary>
    [HttpGet("api/events")]
    public Task<IActionResult> Events(CancellationToken ct) => Forward("api/events", ct);

    /// <summary>Unified activity feed: device events + automation runs + ops logs (roadmap Epic 2G).</summary>
    [HttpGet("api/activity")]
    public Task<IActionResult> Activity(CancellationToken ct) => Forward("api/activity", ct);

    /// <summary>How many activity rows match, without shipping them — for headline counters.</summary>
    [HttpGet("api/activity/count")]
    public Task<IActionResult> ActivityCount(CancellationToken ct) => Forward("api/activity/count", ct);

    /// <summary>Numeric telemetry samples over a period (supports <c>?format=csv</c> export).</summary>
    [HttpGet("api/telemetry")]
    public Task<IActionResult> Telemetry(CancellationToken ct) => Forward("api/telemetry", ct);

    /// <summary>Aggregated telemetry rollups (minute/hour/day) for trend charts (roadmap Epic 1B).</summary>
    [HttpGet("api/telemetry/aggregate")]
    public Task<IActionResult> TelemetryAggregate(CancellationToken ct) => Forward("api/telemetry/aggregate", ct);

    /// <summary>Many (device, capability) telemetry series in one round-trip (dashboard sparklines / composed charts).</summary>
    [HttpPost("api/telemetry/aggregate/batch")]
    public Task<IActionResult> TelemetryAggregateBatch(CancellationToken ct) => Forward("api/telemetry/aggregate/batch", ct);

    /// <summary>Latest event-log row per device in one round-trip (per-tile "last changed by …" provenance).</summary>
    [HttpPost("api/events/latest-by-device")]
    public Task<IActionResult> EventsLatestByDevice(CancellationToken ct) => Forward("api/events/latest-by-device", ct);
}
