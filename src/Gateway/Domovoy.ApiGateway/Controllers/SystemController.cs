// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.ApiGateway.Services;
using Domovoy.Contracts.Messaging;
using Domovoy.Contracts.Security;
using Domovoy.MessageBus;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Domovoy.ApiGateway.Controllers;

/// <summary>
/// System control from the UI (restart services / containers). Two mechanisms:
/// <list type="bullet">
///   <item><b>Self-restart</b> (default, no privileges): publishes <see cref="SystemControlV1"/>; the targeted
///   .NET service stops itself and the container restart policy brings it back.</item>
///   <item><b>Container control</b> (opt-in, <see cref="SystemControlOptions.DockerEnabled"/>): restart/stop/
///   start ANY container over the Docker socket — reaches infra (RabbitMQ/Mongo/Zigbee2MQTT) and hung
///   containers self-restart can't.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/system")]
[Authorize(Policy = WellKnownPermissions.SystemAdmin)]
public class SystemController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly IContainerControl _containers;
    private readonly ILogger<SystemController> _logger;

    // The .NET services that can self-restart over the bus. Name = container/compose name so both the
    // self-restart command and the Docker path address the same thing.
    private static readonly SystemService[] KnownServices =
    {
        new("api-gateway", "gateway"),
        new("db-gateway", "gateway"),
        new("connectivity-service", "service"),
        new("automation-service", "service"),
        new("unified-device-service", "service"),
        new("plugin-supervisor", "service"),
    };

    public SystemController(IMessageBus bus, IContainerControl containers, ILogger<SystemController> logger)
    {
        _bus = bus;
        _containers = containers;
        _logger = logger;
    }

    /// <summary>The manageable surface: self-restartable services (+ live container state when Docker is on).</summary>
    [HttpGet("services")]
    public async Task<IActionResult> GetServices(CancellationToken ct)
    {
        var byName = _containers.Enabled
            ? (await SafeListAsync(ct)).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ContainerInfo>();

        var services = KnownServices.Select(s => new
        {
            s.Name,
            s.Kind,
            selfRestart = true,
            state = byName.TryGetValue(s.Name, out var c) ? c.State : null,
            status = byName.TryGetValue(s.Name, out var c2) ? c2.Status : null,
        });

        // When Docker is on, also surface the infra/other containers the UI can act on but that can't self-restart.
        var knownNames = KnownServices.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = byName.Values
            .Where(c => !knownNames.Contains(c.Name))
            .Select(c => new { c.Name, kind = "container", selfRestart = false, state = (string?)c.State, status = (string?)c.Status });

        return Ok(new { dockerEnabled = _containers.Enabled, services = services.Concat<object>(others) });
    }

    /// <summary>Restart every service (self-restart broadcast). Infra containers are not touched.</summary>
    [HttpPost("restart")]
    public async Task<IActionResult> RestartAll()
    {
        await PublishRestart(SystemControlListener.AllTarget);
        _logger.LogWarning("System restart (all services) requested via UI");
        return Accepted(new { restarted = SystemControlListener.AllTarget, via = "self" });
    }

    /// <summary>
    /// Restart one target. By default a self-restart command; with <c>?container=true</c> (and Docker enabled)
    /// a Docker restart, which also works for infra/hung containers.
    /// </summary>
    [HttpPost("services/{name}/restart")]
    public async Task<IActionResult> RestartService(string name, [FromQuery] bool container = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "service name required" });

        if (container)
        {
            if (!_containers.Enabled) return Conflict(new { error = "Docker container control is disabled" });
            await _containers.RestartAsync(name, ct);
            return Accepted(new { restarted = name, via = "docker" });
        }

        await PublishRestart(name);
        _logger.LogWarning("Restart of '{Service}' requested via UI", name);
        return Accepted(new { restarted = name, via = "self" });
    }

    /// <summary>All containers (Docker path only). 409 when disabled.</summary>
    [HttpGet("containers")]
    public async Task<IActionResult> GetContainers(CancellationToken ct)
    {
        if (!_containers.Enabled) return Conflict(new { error = "Docker container control is disabled" });
        return Ok(await _containers.ListAsync(ct));
    }

    /// <summary>Container lifecycle action (restart|stop|start), Docker path only.</summary>
    [HttpPost("containers/{name}/{action}")]
    public async Task<IActionResult> ContainerAction(string name, string action, CancellationToken ct)
    {
        if (!_containers.Enabled) return Conflict(new { error = "Docker container control is disabled" });

        switch (action.ToLowerInvariant())
        {
            case "restart": await _containers.RestartAsync(name, ct); break;
            case "stop": await _containers.StopAsync(name, ct); break;
            case "start": await _containers.StartAsync(name, ct); break;
            default: return BadRequest(new { error = $"unknown action '{action}' (restart|stop|start)" });
        }

        return Accepted(new { name, action });
    }

    private Task PublishRestart(string target)
    {
        var envelope = Envelope<SystemControlV1>.Create(
            MessageTypes.SystemControl,
            source: "apigateway/system",
            data: new SystemControlV1(target, SystemControlListener.RestartAction),
            subject: target);
        return _bus.PublishAsync(BusTopology.EventsExchange, BusTopology.SystemControlKey, envelope);
    }

    private async Task<IReadOnlyList<ContainerInfo>> SafeListAsync(CancellationToken ct)
    {
        try { return await _containers.ListAsync(ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker container list failed (is the socket mounted?)");
            return Array.Empty<ContainerInfo>();
        }
    }

    private sealed record SystemService(string Name, string Kind);
}
