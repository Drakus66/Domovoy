// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

using Docker.DotNet;
using Docker.DotNet.Models;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>Which topology version is applied, and where it came from.</summary>
public sealed record TopologyState(int Version, string Digest, DateTimeOffset AppliedAt);

/// <summary>
/// Owns the deployment topology on the host (roadmap Epic 3K).
///
/// <para><b>Why this exists.</b> Recreating a container from its own inspect config cannot introduce
/// a new environment variable, volume or service — so without this, every release that changes the
/// compose file would send the owner to the server over SSH. Instead the compose file ships <i>as
/// part of the release</i>: this class fetches the bundle, lays it out under <c>releases/</c>, tops
/// up <c>.env</c> from the template, runs the declared migrations and flips the <c>current</c>
/// symlink. Rolling back is flipping it the other way.</para>
///
/// <para>The split of ownership is deliberate and mirrors <c>/usr</c> versus <c>/etc</c>: the shipped
/// compose is ours and read-only, while <c>.env</c> and <c>docker-compose.override.yml</c> belong to
/// the owner and are never rewritten.</para>
/// </summary>
public sealed class TopologyManager
{
    private readonly UpdaterOptions _options;
    private readonly RegistryClient _registry;
    private readonly DockerClient _docker;
    private readonly ILogger<TopologyManager> _logger;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public TopologyManager(
        UpdaterOptions options, RegistryClient registry, ILogger<TopologyManager> logger)
    {
        _options = options;
        _registry = registry;
        _docker = new DockerClientConfiguration(new Uri(options.DockerSocket)).CreateClient();
        _logger = logger;
    }

    /// <summary>Applied topology, or null on an installation that has never applied one.</summary>
    public TopologyState? GetApplied()
    {
        try
        {
            if (!File.Exists(_options.TopologyStateFile)) return null;
            return JsonSerializer.Deserialize<TopologyState>(File.ReadAllText(_options.TopologyStateFile));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать состояние топологии");
            return null;
        }
    }

    /// <summary>
    /// Represents the topology as an installed component so the resolver can treat
    /// <c>requires: topology</c> like any other dependency, with no special-casing.
    /// </summary>
    public InstalledComponent? AsInstalledComponent()
    {
        var state = GetApplied();
        if (state is null) return null;

        return new InstalledComponent(
            Name: ReleaseSource.TopologyComponent,
            Container: "",
            Repository: ReleaseSource.RepositoryOf(ReleaseSource.TopologyComponent),
            Tag: "",
            Digest: state.Digest,
            Version: $"{state.Version}.0",
            Deps: new ComponentDeps
            {
                Component = ReleaseSource.TopologyComponent,
                Version = $"{state.Version}.0",
                Provides = new Dictionary<string, ProvidedInterface>
                {
                    [WellKnownInterfaces.Topology] = new() { Version = state.Version, MinCompat = 1 },
                },
            });
    }

    /// <summary>
    /// Downloads a bundle by digest and lays it out under <c>releases/topology-N/</c>.
    /// Nothing outside that directory is touched — applying is a separate, reversible step.
    /// </summary>
    public async Task<string> StageAsync(string digest, int version, CancellationToken ct)
    {
        var target = Path.Combine(_options.ReleasesDirectory, $"topology-{version}");

        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        _logger.LogInformation("Скачивание бандла топологии v{Version} ({Digest})", version, digest);

        await using var layer = await _registry.DownloadSingleLayerAsync(
            ReleaseSource.RepositoryOf(ReleaseSource.TopologyComponent), digest, ct);
        await using var gzip = new GZipStream(layer, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, target, overwriteFiles: true, ct);

        if (!File.Exists(Path.Combine(target, "docker-compose.yml")))
            throw new InvalidOperationException("В бандле топологии нет docker-compose.yml.");

        return target;
    }

    /// <summary>
    /// Makes a staged bundle the live one: top up <c>.env</c>, copy shipped configs, run migrations,
    /// flip the <c>current</c> symlink. Returns the keys added to <c>.env</c> so the UI can show them.
    /// </summary>
    public async Task<IReadOnlyList<string>> ApplyAsync(
        string stagedDirectory, int version, string digest, CancellationToken ct)
    {
        var addedKeys = await MergeEnvAsync(stagedDirectory, ct);
        CopyAssets(stagedDirectory);

        var manifestPath = Path.Combine(stagedDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
            if (doc.RootElement.TryGetProperty("hooks", out var hooks) && hooks.ValueKind == JsonValueKind.Array)
                await RunHooksAsync(hooks, ct);
        }

        SwitchCurrent(stagedDirectory);

        Directory.CreateDirectory(_options.StateDirectory);
        await File.WriteAllTextAsync(
            _options.TopologyStateFile,
            JsonSerializer.Serialize(new TopologyState(version, digest, DateTimeOffset.UtcNow), Json),
            ct);

        _logger.LogInformation("Топология переключена на v{Version}", version);
        return addedKeys;
    }

    /// <summary>Points <c>current</c> back at a previous release directory (rollback).</summary>
    public void SwitchCurrent(string releaseDirectory)
    {
        var link = _options.CurrentReleaseLink;

        // Симлинк — не украшение: переключение атомарно и мгновенно обратимо, а прежние релизы
        // остаются на диске, так что откат не требует ничего скачивать.
        if (File.Exists(link) || Directory.Exists(link))
        {
            var info = new FileInfo(link);
            if (info.LinkTarget is not null) File.Delete(link);
            else if (Directory.Exists(link)) Directory.Delete(link, recursive: true);
            else File.Delete(link);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, releaseDirectory);
    }

    private async Task<IReadOnlyList<string>> MergeEnvAsync(string stagedDirectory, CancellationToken ct)
    {
        var templatePath = Path.Combine(stagedDirectory, ".env.template");
        if (!File.Exists(templatePath)) return Array.Empty<string>();

        var template = await File.ReadAllTextAsync(templatePath, ct);
        var existing = File.Exists(_options.EnvFile) ? await File.ReadAllTextAsync(_options.EnvFile, ct) : "";

        var merged = EnvFileMerger.Merge(existing, template);
        if (!merged.Changed) return Array.Empty<string>();

        await File.WriteAllTextAsync(_options.EnvFile, merged.Content, ct);
        _logger.LogInformation("В .env добавлены новые ключи: {Keys}", string.Join(", ", merged.AddedKeys));
        return merged.AddedKeys;
    }

    /// <summary>Config files compose mounts from the host — part of the topology, not owner data.</summary>
    private void CopyAssets(string stagedDirectory)
    {
        var assetsRoot = Path.Combine(stagedDirectory, "assets");
        if (!Directory.Exists(assetsRoot)) return;

        foreach (var source in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(assetsRoot, source);
            var destination = Path.Combine(_options.InstallDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            _logger.LogInformation("Конфиг топологии обновлён: {Path}", relative);
        }
    }

    /// <summary>
    /// Declared migration steps. The vocabulary is intentionally closed — arbitrary shell would make
    /// a failed migration impossible to reason about on someone's house. Every step is idempotent, so
    /// re-running an already-applied topology is a no-op.
    /// </summary>
    private async Task RunHooksAsync(JsonElement hooks, CancellationToken ct)
    {
        foreach (var hook in hooks.EnumerateArray())
        {
            var type = hook.TryGetProperty("type", out var t) ? t.GetString() : null;
            _logger.LogInformation("Шаг миграции топологии: {Type}", type);

            switch (type)
            {
                case "create-dir":
                    Directory.CreateDirectory(
                        Path.Combine(_options.InstallDirectory, hook.GetProperty("path").GetString()!));
                    break;

                case "remove-service":
                    await RemoveServiceAsync(hook.GetProperty("name").GetString()!, ct);
                    break;

                case "rename-volume":
                    await RenameVolumeAsync(
                        hook.GetProperty("from").GetString()!, hook.GetProperty("to").GetString()!, ct);
                    break;

                case "exec-in-service":
                    await ExecInServiceAsync(
                        hook.GetProperty("service").GetString()!,
                        hook.GetProperty("command").EnumerateArray().Select(c => c.GetString()!).ToList(),
                        ct);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Неизвестный шаг миграции топологии: '{type}'. Поддерживаются create-dir, " +
                        "remove-service, rename-volume, exec-in-service.");
            }
        }
    }

    private async Task RemoveServiceAsync(string container, CancellationToken ct)
    {
        try
        {
            await _docker.Containers.StopContainerAsync(container, new ContainerStopParameters(), ct);
            await _docker.Containers.RemoveContainerAsync(container, new ContainerRemoveParameters(), ct);
        }
        catch (DockerContainerNotFoundException)
        {
            // Уже удалён — шаг идемпотентен.
        }
    }

    private async Task RenameVolumeAsync(string from, string to, CancellationToken ct)
    {
        var volumes = await _docker.Volumes.ListAsync(ct);
        if (volumes.Volumes.All(v => v.Name != from)) return; // уже перенесён
        if (volumes.Volumes.Any(v => v.Name == to)) return;

        await _docker.Volumes.CreateAsync(new VolumesCreateParameters { Name = to }, ct);

        // Копируем через одноразовый контейнер из НАШЕГО же образа: он гарантированно есть локально
        // (мы в нём и работаем) и это Debian с coreutils — тянуть отдельный busybox незачем.
        var self = await _docker.Containers.InspectContainerAsync("domovoy-updater", ct);

        var helper = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = self.Image,
            Entrypoint = new List<string> { "/bin/sh", "-c" },
            Cmd = new List<string> { "cp -a /from/. /to/" },
            HostConfig = new HostConfig
            {
                Binds = new List<string> { $"{from}:/from", $"{to}:/to" },
                AutoRemove = true,
            },
        }, ct);

        await _docker.Containers.StartContainerAsync(helper.ID, new ContainerStartParameters(), ct);
        await _docker.Containers.WaitContainerAsync(helper.ID, ct);
        _logger.LogInformation("Том '{From}' перенесён в '{To}'", from, to);
    }

    private async Task ExecInServiceAsync(string container, IList<string> command, CancellationToken ct)
    {
        var exec = await _docker.Exec.ExecCreateContainerAsync(
            container, new ContainerExecCreateParameters { Cmd = command, AttachStdout = true, AttachStderr = true }, ct);

        await _docker.Exec.StartContainerExecAsync(exec.ID, ct);

        var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);
        if (inspect.ExitCode != 0)
            throw new InvalidOperationException(
                $"Шаг миграции в '{container}' завершился с кодом {inspect.ExitCode}: {string.Join(' ', command)}");
    }
}
