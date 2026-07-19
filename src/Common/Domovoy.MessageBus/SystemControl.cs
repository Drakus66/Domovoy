// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Domovoy.Contracts.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Domovoy.MessageBus;

/// <summary>
/// Wires the <see cref="SystemControlListener"/> into a service so a UI-issued "restart" can gracefully stop it.
/// </summary>
public static class SystemControlExtensions
{
    /// <summary>
    /// Registers the system-control listener. <paramref name="serviceName"/> is how the UI addresses this
    /// service and MUST match its container/compose name (e.g. <c>db-gateway</c>, <c>connectivity-service</c>).
    /// </summary>
    public static IServiceCollection AddSystemControl(this IServiceCollection services, string serviceName)
    {
        services.AddSingleton<IHostedService>(sp => new SystemControlListener(
            serviceName,
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILogger<SystemControlListener>>()));
        return services;
    }
}

/// <summary>
/// Listens for the <see cref="SystemControlV1"/> broadcast and, when this service is the target (or the command
/// targets <c>"all"</c>), stops the host gracefully. The container's restart policy (<c>restart: unless-stopped</c>)
/// then brings it back — a restart with no privileged Docker access, purely over the bus. Each service binds its
/// own queue, so a single broadcast reaches every service.
/// </summary>
public sealed class SystemControlListener : BackgroundService
{
    /// <summary>Target value that matches every service.</summary>
    public const string AllTarget = "all";

    /// <summary>The only self-action a service can perform.</summary>
    public const string RestartAction = "restart";

    // Let the publish ack + HTTP response settle before we pull the plug on ourselves.
    private static readonly TimeSpan StopDelay = TimeSpan.FromMilliseconds(750);

    private readonly string _serviceName;
    private readonly IMessageBus _bus;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SystemControlListener> _logger;

    public SystemControlListener(
        string serviceName, IMessageBus bus, IHostApplicationLifetime lifetime, ILogger<SystemControlListener> logger)
    {
        _serviceName = serviceName;
        _bus = bus;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One queue per service (distinct name) so the broadcast fans out to all of them.
        await _bus.SubscribeAsync<Envelope<SystemControlV1>>(
            $"system-control-{_serviceName}",
            BusTopology.EventsExchange,
            BusTopology.SystemControlKey,
            HandleAsync);

        _logger.LogInformation("System-control listener ready for '{Service}'", _serviceName);
    }

    private Task HandleAsync(Envelope<SystemControlV1> envelope)
    {
        var cmd = envelope.Data;
        if (cmd is null) return Task.CompletedTask;

        var targeted = string.Equals(cmd.Target, AllTarget, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(cmd.Target, _serviceName, StringComparison.OrdinalIgnoreCase);
        if (!targeted || !string.Equals(cmd.Action, RestartAction, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        _logger.LogWarning(
            "Restart requested via UI (target '{Target}') — stopping '{Service}'; the container restart policy will bring it back",
            cmd.Target, _serviceName);

        // Stop off the message-handler thread, after a short grace so the requester gets its response.
        _ = Task.Run(async () =>
        {
            await Task.Delay(StopDelay);
            _lifetime.StopApplication();
        });

        return Task.CompletedTask;
    }
}
