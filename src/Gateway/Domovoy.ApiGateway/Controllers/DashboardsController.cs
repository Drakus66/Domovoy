// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for custom dashboard tabs (custom dashboards epic). Forwards CRUD, tab
/// ordering and the hidden-spheres preference to the DbGateway dashboard endpoints
/// (<c>/api/dashboards</c>) over the in-cluster <c>db-gateway</c> HttpClient, mirroring
/// <see cref="ZonesController"/>. Dashboards are pure presentation state (no bus involvement).
/// </summary>
[ApiController]
[Route("api/dashboards")]
public class DashboardsController : ProxyController
{
    public DashboardsController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/dashboards", ct);

    [HttpGet("prefs")]
    public Task<IActionResult> GetPrefs(CancellationToken ct)
        => Forward("api/dashboards/prefs", ct);

    [HttpPut("prefs")]
    public Task<IActionResult> UpdatePrefs(CancellationToken ct)
        => Forward("api/dashboards/prefs", ct);

    [HttpPut("order")]
    public Task<IActionResult> Reorder(CancellationToken ct)
        => Forward("api/dashboards/order", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/dashboards/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/dashboards", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/dashboards/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/dashboards/{Uri.EscapeDataString(id)}", ct);
}
