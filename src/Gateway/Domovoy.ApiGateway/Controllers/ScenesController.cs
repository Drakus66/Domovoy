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
public class ScenesController : ProxyController
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ScenesController> _logger;

    public ScenesController(IHttpClientFactory httpClientFactory, IMessageBus messageBus, ILogger<ScenesController> logger)
        : base(httpClientFactory)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)
        => Forward("api/scenes", ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Get(string id, CancellationToken ct)
        => Forward($"api/scenes/{Uri.EscapeDataString(id)}", ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct)
        => Forward("api/scenes", ct);

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, CancellationToken ct)
        => Forward($"api/scenes/{Uri.EscapeDataString(id)}", ct);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct)
        => Forward($"api/scenes/{Uri.EscapeDataString(id)}", ct);

    /// <summary>
    /// Activate a scene: publish one <see cref="DeviceCommandV1"/> per target device with the captured
    /// capability set. Values are normalized to BCL primitives before publishing (mirroring
    /// <see cref="DeviceControlController"/>) so the owning adapter's codec encodes them cleanly.
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        var client = HttpClientFactory.CreateClient("db-gateway");
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
