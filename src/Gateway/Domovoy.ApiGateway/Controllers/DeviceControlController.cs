// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;

using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Security;
using Domovoy.MessageBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// Capability-addressed device control. Publishes <see cref="DeviceCommandV1"/> on the canonical bus
/// topology; the owning adapter (Zigbee2MQTT, Domovoy Native) encodes the capability set into the
/// device's protocol and applies it.
/// </summary>
[ApiController]
[Route("api/device-control")]
[Authorize(Policy = WellKnownPermissions.DevicesControl)]
public class DeviceControlController : ControllerBase
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<DeviceControlController> _logger;

    public DeviceControlController(IMessageBus messageBus, ILogger<DeviceControlController> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <summary>
    /// Apply a capability set to a device, e.g. body: <c>{ "on_off": true, "brightness": 50 }</c>.
    /// </summary>
    [HttpPost("{id}/set")]
    public async Task<IActionResult> SetCapabilities(string id, [FromBody] Dictionary<string, JsonElement> set)
    {
        if (!Guid.TryParse(id, out var deviceId))
            return BadRequest(new { error = $"deviceId '{id}' is not a valid GUID" });
        if (set is null || set.Count == 0)
            return BadRequest(new { error = "a non-empty capability set is required" });

        var normalized = set.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));

        var envelope = Envelope<DeviceCommandV1>.Create(
            MessageTypes.DeviceCommand,
            source: CommandSource(),
            data: new DeviceCommandV1(deviceId, normalized),
            subject: id);

        await _messageBus.PublishAsync(BusTopology.CommandsExchange, BusTopology.DeviceCommandKey, envelope);

        _logger.LogInformation("Capability command published for {DeviceId}: {Caps}", deviceId, string.Join(", ", set.Keys));
        return Accepted(new { deviceId = id, capabilities = set.Keys });
    }

    /// <summary>
    /// Actor-string for the command's envelope source. When authentication is enforced the id comes from the
    /// verified JWT subject (<c>sub</c>) — real attribution. When auth is off, the WebUI may still send a
    /// <b>self-declared</b> local user (Epic 2E model) in the <c>X-Domovoy-User</c> header — "who of the household
    /// is at this browser". Either way it's <c>user:{id}</c>; falling back to the anonymous <c>apigateway</c>.
    /// </summary>
    private string CommandSource()
    {
        var userId = User.FindFirst("sub")?.Value
                     ?? Request.Headers["X-Domovoy-User"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(userId) || userId.Length > 64 || userId.Contains(':')) return "apigateway";
        return $"user:{userId}";
    }

    private static object? Normalize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        _ => null
    };
}
