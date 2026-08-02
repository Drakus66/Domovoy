// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>What is installed and what the channel offers — everything the resolver needs.</summary>
public sealed record UpdateCatalogSnapshot(
    string Channel,
    IReadOnlyDictionary<string, InstalledComponent> Installed,
    IReadOnlyDictionary<string, IReadOnlyList<AvailableComponent>> Available)
{
    /// <summary>Components with a newer version on the channel — what the UI badges.</summary>
    public IReadOnlyList<string> Outdated => Installed
        .Where(kv => Newest(kv.Key) is { } n && SemVerComparer.IsNewer(n.Version, kv.Value.Version))
        .Select(kv => kv.Key)
        .ToList();

    public AvailableComponent? Newest(string component) =>
        Available.TryGetValue(component, out var list) && list.Count > 0
            ? list.OrderBy(v => v.Version, SemVerComparer.Instance).Last()
            : null;
}

/// <summary>
/// Builds the picture the resolver works from (roadmap Epic 3K): installed components from Docker,
/// the applied topology from disk, and the candidate versions from the registry.
/// <para>
/// Reading the registry is cheap by construction — versions and declared compatibility come from OCI
/// labels via the config blob, so a check costs kilobytes and pulls nothing.
/// </para>
/// </summary>
public sealed class UpdateCatalog
{
    private readonly UpdaterOptions _options;
    private readonly DockerInventory _docker;
    private readonly RegistryClient _registry;
    private readonly TopologyManager _topology;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<UpdateCatalog> _logger;

    public UpdateCatalog(
        UpdaterOptions options,
        DockerInventory docker,
        RegistryClient registry,
        TopologyManager topology,
        IHttpClientFactory httpFactory,
        ILogger<UpdateCatalog> logger)
    {
        _options = options;
        _docker = docker;
        _registry = registry;
        _topology = topology;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// The subscribed channel. Authoritative value lives with the other settings in the database;
    /// <c>.env</c> is the bootstrap fallback for when the gateway is down (and the value the manual
    /// <c>docker compose up -d</c> would use, which is why applying an update writes it back there).
    /// </summary>
    public async Task<string> GetChannelAsync(CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(nameof(UpdateCatalog));
            var settings = await http.GetFromJsonAsync<UpdateSettingsDto>(
                $"{_options.DbGatewayBaseUrl.TrimEnd('/')}/api/update-settings", ct);

            if (UpdateChannels.IsKnown(settings?.Channel)) return settings!.Channel!;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Канал не получен от db-gateway — используем .env");
        }

        try
        {
            if (File.Exists(_options.EnvFile))
            {
                var fromEnv = EnvFileMerger.GetKey(await File.ReadAllTextAsync(_options.EnvFile, ct), "DOMOVOY_CHANNEL");
                if (UpdateChannels.IsKnown(fromEnv)) return fromEnv!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось прочитать канал из .env");
        }

        return UpdateChannels.Release;
    }

    /// <summary>Full snapshot. Registry failures degrade to "nothing available" — offline is normal here.</summary>
    public async Task<UpdateCatalogSnapshot> BuildAsync(CancellationToken ct)
    {
        var channel = await GetChannelAsync(ct);

        var installed = new Dictionary<string, InstalledComponent>(await _docker.GetInstalledAsync(ct));
        if (_topology.AsInstalledComponent() is { } topologyComponent)
            installed[ReleaseSource.TopologyComponent] = topologyComponent;

        var available = new Dictionary<string, IReadOnlyList<AvailableComponent>>();

        var names = ReleaseSource.Components
            .Append(ReleaseSource.TopologyComponent)
            .ToList();

        foreach (var component in names)
        {
            var versions = await _registry.ListAvailableAsync(
                component, channel, _options.MaxVersionsPerComponent, ct);

            if (versions.Count > 0) available[component] = versions;
        }

        return new UpdateCatalogSnapshot(channel, installed, available);
    }

    private sealed record UpdateSettingsDto
    {
        [JsonPropertyName("channel")]
        public string? Channel { get; init; }
    }
}
