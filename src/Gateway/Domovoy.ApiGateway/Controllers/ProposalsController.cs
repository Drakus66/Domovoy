// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the approval queue (roadmap Epic 2C). The queue itself (list/create/approve/reject)
/// lives in the DbGateway, which owns the target collections and applies the side-effects; the heuristic
/// proposer's manual scan runs on the AutomationService (which reads the event-log and owns the blocks).
/// Mirrors <see cref="MlController"/> in fronting two upstreams behind one route.
/// </summary>
[ApiController]
[Route("api/proposals")]
public class ProposalsController : ProxyController
{
    public ProposalsController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => ForwardTo("db-gateway", "api/proposals", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => ForwardTo("db-gateway", $"api/proposals/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => ForwardTo("db-gateway", "api/proposals", ct);

    [HttpPost("{id}/approve")]
    public Task<IActionResult> Approve(string id, CancellationToken ct)
        => ForwardTo("db-gateway", $"api/proposals/{Uri.EscapeDataString(id)}/approve", ct);

    [HttpPost("{id}/reject")]
    public Task<IActionResult> Reject(string id, CancellationToken ct)
        => ForwardTo("db-gateway", $"api/proposals/{Uri.EscapeDataString(id)}/reject", ct);

    /// <summary>Run the heuristic proposer now (Epic 2C stub-precursor to 2F); mines the event-log for candidates.</summary>
    [HttpPost("suggest")]
    public Task<IActionResult> Suggest(CancellationToken ct)
        => ForwardTo("automation-service", "api/proposals/suggest", ct);

    /// <summary>Run the pattern-discovery engine now (Epic 2F); the full MI/FDR funnel over history → queued proposals.</summary>
    [HttpPost("discover")]
    public Task<IActionResult> Discover(CancellationToken ct)
        => ForwardTo("automation-service", "api/discovery/scan", ct);
}
