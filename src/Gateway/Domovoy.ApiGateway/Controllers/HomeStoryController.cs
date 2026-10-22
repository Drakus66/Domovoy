// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the House Diary (roadmap Epic 2N): the materialized diary feed, on-the-fly preview,
/// manual rebuild, and the personalization overrides — all served by the DbGateway. Forwards over the
/// in-cluster <c>db-gateway</c> HttpClient, mirroring <see cref="HistoryController"/> / <see cref="UsersController"/>.
/// </summary>
[ApiController]
public class HomeStoryController : ProxyController
{
    public HomeStoryController(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [HttpGet("api/home-story")]
    public Task<IActionResult> Feed(CancellationToken ct) => Forward("api/home-story", ct);

    [HttpPost("api/home-story/preview")]
    public Task<IActionResult> Preview(CancellationToken ct) => Forward("api/home-story/preview", ct);

    [HttpPost("api/home-story/rebuild")]
    public Task<IActionResult> Rebuild(CancellationToken ct) => Forward("api/home-story/rebuild", ct);

    [HttpGet("api/narrative-entities")]
    public Task<IActionResult> Entities(CancellationToken ct) => Forward("api/narrative-entities", ct);

    [HttpPost("api/narrative-entities")]
    public Task<IActionResult> CreateEntity(CancellationToken ct) => Forward("api/narrative-entities", ct);

    [HttpPut("api/narrative-entities/{id}")]
    public Task<IActionResult> UpdateEntity(string id, CancellationToken ct)
        => Forward($"api/narrative-entities/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("api/narrative-entities/{id}")]
    public Task<IActionResult> DeleteEntity(string id, CancellationToken ct)
        => Forward($"api/narrative-entities/{Uri.EscapeDataString(id)}", ct);
}
