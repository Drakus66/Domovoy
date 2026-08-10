// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the electrical topology (roadmap Epic 3C-D) served by the DbGateway
/// (<c>/api/power-topology</c>): supplies, panels and circuits with their phase and breaker rating.
/// Mirrors <see cref="ZonesController"/> — the request body is forwarded verbatim.
/// </summary>
[ApiController]
[Route("api/power-topology")]
public class PowerTopologyController : ProxyController
{
    public PowerTopologyController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    /// <summary>All topology nodes (flat; the client resolves the tree through parentId).</summary>
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => Forward("api/power-topology", ct);

    /// <summary>Create a node (supply / panel / circuit).</summary>
    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct) => Forward("api/power-topology", ct);

    /// <summary>Update a node.</summary>
    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/power-topology/{Uri.EscapeDataString(id)}", ct);

    /// <summary>Delete a node; its children move up and its devices are detached.</summary>
    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/power-topology/{Uri.EscapeDataString(id)}", ct);
}
