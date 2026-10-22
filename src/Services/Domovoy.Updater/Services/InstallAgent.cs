// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Security.Cryptography;

using Docker.DotNet;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// First installation of a house (roadmap Epic 3K): lays out an empty installation directory from the
/// channel's topology bundle and brings the stack up — the same steps a topology update performs, so the
/// first install and every later update share one tested path.
///
/// <para><b>Why it exists.</b> There was no first-install path at all: only applying a topology update copied
/// the bundle's assets, so a hand-made install mounted a missing <c>prometheus.yaml</c>, Docker created a
/// directory in its place, and the container failed to start. Secrets stayed at their template
/// placeholders.</para>
///
/// <code>
/// docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v /opt/domovoy:/opt/domovoy \
///   ghcr.io/drakus66/domovoy-updater:release --install [--channel dev]
/// </code>
///
/// <para>The host path must equal the container path: compose runs inside the container, but Docker resolves
/// the bind sources it names (<c>/opt/domovoy/data/…</c>) on the host.</para>
/// </summary>
public static class InstallAgent
{
    public const string Switch = "--install";
    public const string ChannelSwitch = "--channel";

    /// <summary>Template keys that hold secrets: a placeholder value is replaced with a random one.</summary>
    private static readonly string[] GeneratedSecrets =
        { "RABBITMQ_DEFAULT_PASS", "JWT_SECRET_KEY", "DOMOVOY_ADMIN_PASSWORD" };

    private const string Placeholder = "change-me";

    public static async Task<int> RunAsync(string[] args, UpdaterOptions options, CancellationToken ct)
    {
        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var log = loggerFactory.CreateLogger("install");

        var channel = ChannelFrom(args);
        if (channel is null)
        {
            log.LogError("Неизвестный канал. Допустимы '{Release}' и '{Dev}'.", UpdateChannels.Release, UpdateChannels.Dev);
            return 2;
        }

        // Повторная установка поверх живого дома перезаписала бы .env владельца и сбила пины —
        // для уже установленного дома есть обновление, а не установка.
        if (File.Exists(options.TopologyStateFile) || Directory.Exists(options.CurrentReleaseLink))
        {
            log.LogError("В {Directory} уже есть установка — используйте обновление из /settings.", options.InstallDirectory);
            return 3;
        }

        if (!await HostPathMatchesAsync(options, log, ct)) return 4;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var registry = new RegistryClient(http, loggerFactory.CreateLogger<RegistryClient>());

        var bundle = (await registry.ListAvailableAsync(ReleaseSource.TopologyComponent, channel, 1, ct)).FirstOrDefault();
        if (bundle is null)
        {
            log.LogError("В канале '{Channel}' нет бандла топологии — устанавливать нечего.", channel);
            return 5;
        }

        log.LogInformation("Установка из канала '{Channel}': топология {Version}", channel, bundle.Version);

        var topology = new TopologyManager(options, registry, loggerFactory.CreateLogger<TopologyManager>());
        var version = UpdateExecutor.ParseMajor(bundle.Version);
        var staged = await topology.StageAsync(bundle.Digest, version, ct);

        // Применение доливает .env из шаблона, раскладывает ассеты, прогоняет хуки и переключает current —
        // ровно то же, что при обновлении топологии.
        await topology.ApplyAsync(staged, version, bundle.Digest, bundle.Tag, ct);

        var adminPassword = await FillSecretsAsync(options, channel, log, ct);

        log.LogInformation("Запуск стека…");
        var compose = new ComposeRunner(options, loggerFactory.CreateLogger<ComposeRunner>());
        log.LogInformation("{Output}", await compose.UpAllExceptAsync(excluded: "", ct));

        log.LogInformation("Готово. Вход: admin / {Password} — смените пароль после первого входа.", adminPassword);
        return 0;
    }

    private static string? ChannelFrom(string[] args)
    {
        var index = Array.IndexOf(args, ChannelSwitch);
        if (index < 0) return UpdateChannels.Release;
        return index + 1 < args.Length && UpdateChannels.IsKnown(args[index + 1]) ? args[index + 1] : null;
    }

    /// <summary>
    /// Replaces template placeholders of secret keys with random values and records the channel, so the
    /// house starts with secrets nobody else has and polls the channel its images came from.
    /// </summary>
    private static async Task<string> FillSecretsAsync(
        UpdaterOptions options, string channel, ILogger log, CancellationToken ct)
    {
        var env = await File.ReadAllTextAsync(options.EnvFile, ct);
        env = EnvFileMerger.SetKey(env, "DOMOVOY_CHANNEL", channel);

        foreach (var key in GeneratedSecrets)
        {
            var current = EnvFileMerger.GetKey(env, key);
            if (current is null || current.StartsWith(Placeholder, StringComparison.Ordinal))
                env = EnvFileMerger.SetKey(env, key, Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant());
        }

        await File.WriteAllTextAsync(options.EnvFile, env, ct);

        // Остальные заглушки (путь к Zigbee-стику и т.п.) сгенерировать нельзя — их задаёт владелец.
        foreach (var line in env.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith('#') || !line.Contains(Placeholder, StringComparison.Ordinal)) continue;
            log.LogWarning("В .env осталась заглушка — задайте значение вручную: {Line}", line);
        }

        return EnvFileMerger.GetKey(env, "DOMOVOY_ADMIN_PASSWORD")!;
    }

    /// <summary>
    /// Refuses a mount whose host path differs from the container path: compose would then point every bind
    /// at a host directory that is not the installation, and the house would come up with empty state.
    /// </summary>
    private static async Task<bool> HostPathMatchesAsync(UpdaterOptions options, ILogger log, CancellationToken ct)
    {
        try
        {
            using var docker = new DockerClientConfiguration(new Uri(options.DockerSocket)).CreateClient();
            var self = await docker.Containers.InspectContainerAsync(Environment.MachineName, ct);
            var mount = self.Mounts.FirstOrDefault(m => m.Destination == options.InstallDirectory);

            if (mount is null)
            {
                log.LogError("Каталог установки {Directory} не смонтирован с хоста.", options.InstallDirectory);
                return false;
            }

            if (mount.Source != mount.Destination)
            {
                log.LogError(
                    "Каталог установки смонтирован из {Source}, а должен из {Destination}: compose внутри контейнера " +
                    "называет пути хоста, и они обязаны совпадать.", mount.Source, mount.Destination);
                return false;
            }

            return true;
        }
        catch (DockerApiException ex)
        {
            // Контейнер запущен с --hostname или Docker недоступен по имени — проверить нельзя, не блокируем.
            log.LogWarning("Не удалось проверить монтирование каталога установки: {Message}", ex.Message);
            return true;
        }
    }
}
