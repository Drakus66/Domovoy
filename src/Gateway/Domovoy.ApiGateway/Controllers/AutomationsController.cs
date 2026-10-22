// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for automation rule management + run history (roadmap Epic 1A). Forwards CRUD,
/// status toggles and history queries to the DbGateway <c>/api/automations</c> endpoints, mirroring
/// <see cref="ZonesController"/>. The AutomationService loads rules directly from the DbGateway; this
/// proxy is for the WebUI.
/// </summary>
[ApiController]
[Route("api/automations")]
public class AutomationsController : ProxyController
{
    public AutomationsController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/automations", ct);

    [HttpGet("history")]
    public Task<IActionResult> History(CancellationToken ct)
        => Forward("api/automations/history", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/automations/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/automations", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/automations/{Uri.EscapeDataString(id)}", ct);

    [HttpPut("{id}/status")]
    public Task<IActionResult> SetStatus(string id, CancellationToken ct)
        => Forward($"api/automations/{Uri.EscapeDataString(id)}/status", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/automations/{Uri.EscapeDataString(id)}", ct);
}
