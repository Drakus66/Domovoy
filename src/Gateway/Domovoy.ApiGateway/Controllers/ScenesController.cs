// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.MessageBus;

using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Scene management + activation (roadmap Epic 3B). CRUD is a thin reverse proxy to the DbGateway
/// <c>/api/scenes</c> endpoints (mirroring <see cref="AutomationsController"/>); activation loads the
/// scene and fans its per-device target states out as <see cref="DeviceCommandV1"/> on the canonical bus
/// topology — the same actuation path manual control uses (<see cref="DeviceControlController"/>) — with
/// source <c>scene:{id}</c> so the P0-5 event-log attributes each resulting change to the scene.
/// </summary>
[ApiController]
[Route("api/scenes")]
public class ScenesController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ScenesController> _logger;

    public ScenesController(IHttpClientFactory httpClientFactory, IMessageBus messageBus, ILogger<ScenesController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _messageBus = messageBus;
        _logger = logger;
    }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward(HttpMethod.Get, "api/scenes", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward(HttpMethod.Get, $"api/scenes/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward(HttpMethod.Post, "api/scenes", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward(HttpMethod.Put, $"api/scenes/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward(HttpMethod.Delete, $"api/scenes/{Uri.EscapeDataString(id)}", ct);

    /// <summary>
    /// Activate a scene: publish one <see cref="DeviceCommandV1"/> per target device with the captured
    /// capability set. Values are normalized to BCL primitives before publishing (mirroring
    /// <see cref="DeviceControlController"/>) so the owning adapter's codec encodes them cleanly.
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("db-gateway");
        using var upstream = await client.GetAsync($"api/scenes/{Uri.EscapeDataString(id)}", ct);
        if (upstream.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (!upstream.IsSuccessStatusCode) return StatusCode((int)upstream.StatusCode);

        var scene = await upstream.Content.ReadFromJsonAsync<SceneDto>(cancellationToken: ct);
        if (scene is null) return NotFound();

        var source = $"scene:{id}";
        var devices = 0;
        foreach (var target in scene.Targets)
        {
            if (!Guid.TryParse(target.DeviceId, out var deviceId) || target.Set is null || target.Set.Count == 0)
                continue;

            var normalized = target.Set.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));
            var envelope = Envelope<DeviceCommandV1>.Create(
                MessageTypes.DeviceCommand,
                source: source,
                data: new DeviceCommandV1(deviceId, normalized),
                subject: target.DeviceId,
                correlationId: id);
            await _messageBus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope, ct);
            devices++;
        }

        _logger.LogInformation("Scene {SceneId} activated → {Devices} device command(s)", id, devices);
        return Accepted(new { sceneId = id, devices });
    }

    private async Task<IActionResult> Forward(HttpMethod method, string path, CancellationToken ct)
    {
        var relativePath = Request.QueryString.HasValue ? $"{path}{Request.QueryString.Value}" : path;
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

    private static object? Normalize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        _ => null
    };

    private sealed record SceneDto(string Id, string Name, List<SceneTargetDto> Targets);
    private sealed record SceneTargetDto(string DeviceId, Dictionary<string, JsonElement> Set);
}
