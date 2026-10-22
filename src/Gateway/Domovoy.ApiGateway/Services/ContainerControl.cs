// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Docker.DotNet;
using Docker.DotNet.Models;

namespace Domovoy.ApiGateway.Services;

/// <summary>Options for the opt-in Docker-socket container control (System page).</summary>
public sealed class SystemControlOptions
{
    public const string Section = "SystemControl";

    /// <summary>
    /// When true, the gateway talks to the Docker daemon over <see cref="DockerSocket"/> to list/restart/stop/
    /// start ANY container (infra included). OFF by default: it needs <c>/var/run/docker.sock</c> mounted, which
    /// is root-equivalent access — enable only on a trusted host, ideally not internet-exposed.
    /// </summary>
    public bool DockerEnabled { get; set; }

    /// <summary>Docker daemon endpoint. Default is the Linux socket the container would mount.</summary>
    public string DockerSocket { get; set; } = "unix:///var/run/docker.sock";
}

/// <summary>One container's coarse state, for the System page.</summary>
public sealed record ContainerInfo(string Name, string State, string Status, string Image);

/// <summary>
/// Container lifecycle control. The <b>self-restart</b> path (a service stops itself over the bus, the restart
/// policy brings it back) needs none of this; this is the opt-in, privileged path that can also reach infra
/// containers (RabbitMQ/Mongo/Zigbee2MQTT) and containers too hung to self-restart.
/// </summary>
public interface IContainerControl
{
    bool Enabled { get; }
    Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct);
    Task RestartAsync(string name, CancellationToken ct);
    Task StopAsync(string name, CancellationToken ct);
    Task StartAsync(string name, CancellationToken ct);
}

/// <summary>Default when Docker control is off — every action is a no-op that reports "disabled".</summary>
public sealed class DisabledContainerControl : IContainerControl
{
    public bool Enabled => false;
    public Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());
    public Task RestartAsync(string name, CancellationToken ct) => throw Disabled();
    public Task StopAsync(string name, CancellationToken ct) => throw Disabled();
    public Task StartAsync(string name, CancellationToken ct) => throw Disabled();
    private static InvalidOperationException Disabled() => new("Docker container control is disabled");
}

/// <summary>Talks to the Docker daemon over the socket (enabled path). Containers are addressed by name.</summary>
public sealed class DockerContainerControl : IContainerControl, IDisposable
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerControl> _logger;

    public bool Enabled => true;

    public DockerContainerControl(SystemControlOptions options, ILogger<DockerContainerControl> logger)
    {
        _client = new DockerClientConfiguration(new Uri(options.DockerSocket)).CreateClient();
        _logger = logger;
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListAsync(CancellationToken ct)
    {
        var list = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);
        return list
            .Select(c => new ContainerInfo(
                Name: c.Names.FirstOrDefault()?.TrimStart('/') ?? (c.ID.Length >= 12 ? c.ID[..12] : c.ID),
                State: c.State ?? "unknown",
                Status: c.Status ?? string.Empty,
                Image: c.Image ?? string.Empty))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task RestartAsync(string name, CancellationToken ct)
    {
        _logger.LogWarning("Docker: restarting container '{Name}'", name);
        return _client.Containers.RestartContainerAsync(name, new ContainerRestartParameters(), ct);
    }

    public async Task StopAsync(string name, CancellationToken ct)
    {
        _logger.LogWarning("Docker: stopping container '{Name}'", name);
        await _client.Containers.StopContainerAsync(name, new ContainerStopParameters(), ct);
    }

    public Task StartAsync(string name, CancellationToken ct)
    {
        _logger.LogWarning("Docker: starting container '{Name}'", name);
        return _client.Containers.StartContainerAsync(name, new ContainerStartParameters(), ct);
    }

    public void Dispose() => _client.Dispose();
}
