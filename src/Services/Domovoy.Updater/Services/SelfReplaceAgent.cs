// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Docker.DotNet;
using Docker.DotNet.Models;

using Domovoy.Updater.Configuration;

namespace Domovoy.Updater.Services;

/// <summary>
/// The one-shot mode that replaces the running updater (roadmap Epic 3K).
///
/// <para><b>Why a separate process at all.</b> A container cannot recreate itself: the instant it is
/// stopped, the code doing the recreating stops with it, leaving the system without an updater. So
/// the final step of an update starts a throwaway container from the <i>new</i> updater image, which
/// waits for the old one to exit and brings the service back through compose. Then it removes itself
/// (<c>AutoRemove</c>). This is the same shape watchtower uses, for the same reason.</para>
///
/// <para>Runs before the web host is even built — see <c>Program.cs</c> — so this mode needs nothing
/// from the application: no bus, no database, no HTTP.</para>
/// </summary>
public static class SelfReplaceAgent
{
    /// <summary>Command-line switch that selects this mode.</summary>
    public const string Switch = "--self-replace";

    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(string container, UpdaterOptions options, CancellationToken ct)
    {
        Console.WriteLine($"[self-replace] Ожидание остановки '{container}'…");

        using var docker = new DockerClientConfiguration(new Uri(options.DockerSocket)).CreateClient();

        // Останавливаем предшественника сами: он уже отчитался об успехе и ждёт замены.
        try
        {
            await docker.Containers.StopContainerAsync(
                container, new ContainerStopParameters { WaitBeforeKillSeconds = 30 }, ct);
        }
        catch (DockerContainerNotFoundException)
        {
            Console.WriteLine("[self-replace] Предшественник уже отсутствует.");
        }

        var deadline = DateTimeOffset.UtcNow + StopTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var inspect = await docker.Containers.InspectContainerAsync(container, ct);
                if (!inspect.State.Running) break;
            }
            catch (DockerContainerNotFoundException)
            {
                break;
            }

            await Task.Delay(PollInterval, ct);
        }

        Console.WriteLine("[self-replace] Пересоздание службы обновлений…");

        // Тот же compose, что и у обычного шага: пин уже переписан основным процессом до его смерти,
        // поэтому здесь достаточно поднять сервис.
        var runner = new ComposeRunner(options, new ConsoleLogger<ComposeRunner>());
        var output = await runner.UpAsync(new[] { container }, ct);

        Console.WriteLine(output);
        Console.WriteLine("[self-replace] Готово.");
        return 0;
    }

    /// <summary>
    /// Minimal logger for the agent: it runs outside the host, so the DI-provided logging is not
    /// available and stdout is the only place anything can go.
    /// </summary>
    private sealed class ConsoleLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Console.WriteLine($"[self-replace] {formatter(state, exception)}");
    }
}
