// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the home mode / presence context (roadmap Epic 1G), mirroring
/// <see cref="ZonesController"/>. Forwards reads and the manual switch to the DbGateway
/// <c>/api/mode</c> endpoints, which persist the mode and publish the change on the bus.
/// </summary>
[ApiController]
[Route("api/mode")]
public class ModeController : ProxyController
{
    public ModeController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct)
        => Forward("api/mode", ct);

    [HttpGet("options")]
    public Task<IActionResult> Options(CancellationToken ct)
        => Forward("api/mode/options", ct);

    [HttpPut]
    public Task<IActionResult> Set(CancellationToken ct)
        => Forward("api/mode", ct);
}
