// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for control blocks (roadmap Epic 1H). CRUD is forwarded to the DbGateway
/// <c>/api/blocks</c> (persistence), while the type <c>catalog</c> comes from the AutomationService
/// (which owns the block runtime + registered types). Mirrors <see cref="AutomationsController"/>.
/// </summary>
[ApiController]
[Route("api/blocks")]
public class BlocksController : ProxyController
{
    public BlocksController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    // The type catalog lives in the AutomationService. Declared before {id} so "catalog" isn't taken as an id.
    [HttpGet("catalog")]
    public Task<IActionResult> Catalog(CancellationToken ct)
        => ForwardTo("automation-service", "api/blocks/catalog", ct);

    // Runtime health (last-tick/error per block) also lives in the AutomationService (owns the runtime).
    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct)
        => ForwardTo("automation-service", "api/blocks/status", ct);

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => ForwardTo("db-gateway", "api/blocks", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => ForwardTo("db-gateway", $"api/blocks/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => ForwardTo("db-gateway", "api/blocks", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => ForwardTo("db-gateway", $"api/blocks/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => ForwardTo("db-gateway", $"api/blocks/{Uri.EscapeDataString(id)}", ct);
}
