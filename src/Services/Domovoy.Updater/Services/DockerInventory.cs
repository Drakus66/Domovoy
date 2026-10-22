// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using Docker.DotNet;
using Docker.DotNet.Models;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// What is actually running on this host (roadmap Epic 3K).
/// <para>
/// Reads each component's container and the image behind it, and takes the declared compatibility
/// from the image's own <c>ru.domovoy.deps</c> label — not from any local bookkeeping. The image is
/// the source of truth about itself, so a container recreated by hand still reports honestly.
/// </para>
/// </summary>
public sealed class DockerInventory : IDisposable
{
    private readonly DockerClient _docker;
    private readonly ILogger<DockerInventory> _logger;

    public DockerInventory(UpdaterOptions options, ILogger<DockerInventory> logger)
    {
        _docker = new DockerClientConfiguration(new Uri(options.DockerSocket)).CreateClient();
        _logger = logger;
    }

    /// <summary>Installed components, keyed by component name. Missing containers are simply absent.</summary>
    public async Task<IReadOnlyDictionary<string, InstalledComponent>> GetInstalledAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, InstalledComponent>();

        foreach (var component in ReleaseSource.Components)
        {
            var container = ReleaseSource.ContainerOf(component);
            try
            {
                var inspect = await _docker.Containers.InspectContainerAsync(container, ct);
                var image = await _docker.Images.InspectImageAsync(inspect.Image, ct);

                // Docker.DotNet отдаёт IDictionary, у которого нет GetValueOrDefault — копируем в
                // конкретный словарь, чтобы дальше работать привычно.
                var labels = new Dictionary<string, string>(
                    image.Config?.Labels ?? new Dictionary<string, string>());
                var deps = ComponentDeps.TryParse(labels.GetValueOrDefault("ru.domovoy.deps"));

                var (repository, tag) = SplitReference(inspect.Config?.Image);
                var digest = image.RepoDigests?.FirstOrDefault()?.Split('@').LastOrDefault() ?? image.ID;

                result[component] = new InstalledComponent(
                    Name: component,
                    Container: container,
                    Repository: repository,
                    Tag: tag,
                    Digest: digest ?? "",
                    // Локально собранный образ метки не несёт: он честно репортит 0.0.0, и это
                    // сигнал «собрано здесь», а не пришло из канала.
                    Version: deps?.Version ?? labels.GetValueOrDefault("org.opencontainers.image.version") ?? "0.0.0",
                    Deps: deps ?? new ComponentDeps { Component = component, Container = container });
            }
            catch (DockerContainerNotFoundException)
            {
                _logger.LogDebug("Контейнер '{Container}' не найден — компонент не установлен", container);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось прочитать состояние компонента '{Component}'", component);
            }
        }

        return result;
    }

    /// <summary>Whether the Docker daemon is reachable at all — the first thing preflight checks.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            await _docker.System.PingAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Docker недоступен: смонтирован ли /var/run/docker.sock?");
            return false;
        }
    }

    /// <summary>Pull an image by digest. Progress is coarse — the UI shows per-component steps.</summary>
    public async Task PullAsync(string repository, string digest, CancellationToken ct)
    {
        _logger.LogInformation("Pull {Registry}/{Repository}@{Digest}", ReleaseSource.Registry, repository, digest);

        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = $"{ReleaseSource.Registry}/{repository}",
                Tag = digest, // Docker принимает digest в поле tag — так тянется content-addressed образ
            },
            authConfig: null,
            new Progress<JSONMessage>(m =>
            {
                if (!string.IsNullOrWhiteSpace(m.ErrorMessage))
                    _logger.LogWarning("Pull: {Error}", m.ErrorMessage);
            }),
            ct);
    }

    /// <summary>
    /// Tag a pulled digest with a local name. The generated pin overlay refers to images by
    /// <c>repo@sha256:…</c>, so this is only used where a readable name helps in logs.
    /// </summary>
    public Task TagAsync(string repository, string digest, string tag, CancellationToken ct) =>
        _docker.Images.TagImageAsync(
            $"{ReleaseSource.Registry}/{repository}@{digest}",
            new ImageTagParameters { RepositoryName = $"{ReleaseSource.Registry}/{repository}", Tag = tag },
            ct);

    private static (string Repository, string Tag) SplitReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return ("", "");

        var withoutDigest = reference.Split('@')[0];
        var lastColon = withoutDigest.LastIndexOf(':');
        var lastSlash = withoutDigest.LastIndexOf('/');

        // Двоеточие до последнего слэша — это порт реестра, а не тег.
        if (lastColon > lastSlash)
            return (withoutDigest[..lastColon], withoutDigest[(lastColon + 1)..]);

        return (withoutDigest, "latest");
    }

    public void Dispose() => _docker.Dispose();
}
