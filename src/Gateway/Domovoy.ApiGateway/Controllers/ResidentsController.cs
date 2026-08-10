// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the tracked-resident roster (roadmap Epic 3D — presence as a platform signal).
/// Forwards CRUD to the DbGateway resident endpoints (<c>/api/residents</c>) over the in-cluster
/// <c>db-gateway</c> HttpClient, mirroring <see cref="UsersController"/>. Gated on <c>users.manage</c> —
/// the resident roster is household-member configuration, the same admin surface as users.
/// </summary>
[ApiController]
[Route("api/residents")]
[Authorize(Policy = WellKnownPermissions.UsersManage)]
public class ResidentsController : ProxyController
{
    public ResidentsController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/residents", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/residents/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/residents", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/residents/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/residents/{Uri.EscapeDataString(id)}", ct);
}
