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
public class ResidentsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ResidentsController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/residents", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/residents/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/residents", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/residents/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/residents/{Uri.EscapeDataString(id)}", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string relativePath, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var request = new HttpRequestMessage(method, relativePath);

        if (method == HttpMethod.Post || method == HttpMethod.Put)
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var responseBody = await upstream.Content.ReadAsStringAsync(ct);

        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            Content = string.IsNullOrEmpty(responseBody) ? null : responseBody,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
        };
    }
}
