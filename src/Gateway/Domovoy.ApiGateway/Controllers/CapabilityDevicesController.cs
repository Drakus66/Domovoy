// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Thin reverse proxy for the capability device read-model. Forwards requests to the DbGateway
/// read endpoints (<c>/api/capability-devices</c>). Replaces the former single Ocelot route now
/// that the gateway is uniformly endpoint-routed — there is no terminal Ocelot middleware to
/// shadow the in-process controllers and SignalR hub.
/// </summary>
[ApiController]
[Route("api/capability-devices")]
public class CapabilityDevicesController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CapabilityDevicesController(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <summary>List the read-model, optionally filtered by <paramref name="zoneId"/> (P0-3).</summary>
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? zoneId, CancellationToken ct)
    {
        var path = zoneId is null
            ? "api/capability-devices"
            : $"api/capability-devices?zoneId={Uri.EscapeDataString(zoneId)}";
        return Forward(HttpMethod.Get, path, ct);
    }

    /// <summary>Fetch a single capability device by id.</summary>
    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/capability-devices/{Uri.EscapeDataString(id)}", ct);

    /// <summary>Bind the device to a zone (or unassign). Body: <c>{ "zoneId": "..." }</c> (P0-3).</summary>
    [HttpPut("{id}/zone")]
    public Task<IActionResult> AssignZone(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/capability-devices/{Uri.EscapeDataString(id)}/zone", ct);

    /// <summary>Set/clear the user-set friendly name (Epic 3G-alias). Body: <c>{ "alias": "Chandelier" | null }</c>.</summary>
    [HttpPut("{id}/alias")]
    public Task<IActionResult> SetAlias(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/capability-devices/{Uri.EscapeDataString(id)}/alias", ct);

    /// <summary>Set/clear the manual archetype override (Epic 2D). Body: <c>{ "archetype": "light" | null }</c>.</summary>
    [HttpPut("{id}/archetype")]
    public Task<IActionResult> SetArchetype(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/capability-devices/{Uri.EscapeDataString(id)}/archetype", ct);

    /// <summary>Set/clear the device's energy profile (Epic 3C-D). Body:
    /// <c>{ "energyProfile": { "track": true, "role": "mains", "maxPowerW": 60, ... } | null }</c>.</summary>
    [HttpPut("{id}/energy-profile")]
    public Task<IActionResult> SetEnergyProfile(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/capability-devices/{Uri.EscapeDataString(id)}/energy-profile", ct);

    /// <summary>Set/clear the load-shedding profile (Epic 3C-LM). Body: <c>{ "loadShedding": {...} | null }</c>.</summary>
    [HttpPut("{id}/load-shedding")]
    public Task<IActionResult> SetLoadShedding(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/capability-devices/{Uri.EscapeDataString(id)}/load-shedding", ct);

    /// <summary>Delete an offline device from the registry (409 while online). A re-announce recreates it.</summary>
    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/capability-devices/{Uri.EscapeDataString(id)}", ct);

    private async Task<IActionResult> Forward(HttpMethod method, string relativePath, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var request = new HttpRequestMessage(method, relativePath);

        if (method == HttpMethod.Put || method == HttpMethod.Post)
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
