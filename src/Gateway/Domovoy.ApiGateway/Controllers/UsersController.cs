// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for local user management (roadmap Epic 2E). Forwards CRUD to the DbGateway user
/// endpoints (<c>/api/users</c>) over the in-cluster <c>db-gateway</c> HttpClient, mirroring
/// <see cref="ZonesController"/>. Gated on <c>users.manage</c> — managing accounts (incl. password resets)
/// is an admin surface; individual self-service password change goes through AuthController instead.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = WellKnownPermissions.UsersManage)]
public class UsersController : ProxyController
{
    public UsersController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/users", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/users/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/users", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/users/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/users/{Uri.EscapeDataString(id)}", ct);

    // Admin password reset / initial-password set for another user. The DbGateway treats an omitted
    // CurrentPassword as a reset; this endpoint is already gated on users.manage by the controller policy.
    [HttpPut("{id}/password")]
    public Task<IActionResult> SetPassword(string id, CancellationToken ct)
        => Forward($"api/users/{Uri.EscapeDataString(id)}/password", ct);
}
