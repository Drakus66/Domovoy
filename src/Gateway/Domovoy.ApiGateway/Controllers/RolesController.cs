// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for role management (roadmap Epic 2E). Forwards CRUD and the permission vocabulary to
/// the DbGateway role endpoints (<c>/api/roles</c>) over the in-cluster <c>db-gateway</c> HttpClient, mirroring
/// <see cref="ZonesController"/>. Gated on <c>users.manage</c> (roles and users are one admin surface).
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize(Policy = WellKnownPermissions.UsersManage)]
public class RolesController : ProxyController
{
    public RolesController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/roles", ct);

    [HttpGet("permissions")]
    public Task<IActionResult> Permissions(CancellationToken ct)
        => Forward("api/roles/permissions", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/roles/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/roles", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/roles/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/roles/{Uri.EscapeDataString(id)}", ct);
}
