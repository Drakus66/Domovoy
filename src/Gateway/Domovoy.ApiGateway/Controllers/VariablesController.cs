// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for global-variable management (roadmap Epic 3E, Hubitat "Hub Variables"). Forwards
/// CRUD to the DbGateway <c>/api/variables</c> endpoints, mirroring <see cref="AutomationsController"/>.
/// The AutomationService reads variables directly from the DbGateway; this proxy is for the WebUI.
/// </summary>
[ApiController]
[Route("api/variables")]
public class VariablesController : ProxyController
{
    public VariablesController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/variables", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/variables/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/variables", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/variables/{Uri.EscapeDataString(id)}", ct);

    [HttpPut("{id}/value")]
    public Task<IActionResult> SetValue(string id, CancellationToken ct)
        => Forward($"api/variables/{Uri.EscapeDataString(id)}/value", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/variables/{Uri.EscapeDataString(id)}", ct);
}
