// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Domovoy.Updater.Configuration;
using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// How this house polls the registry (roadmap Epic 3K) — the operator's live choices, not the container's
/// environment. Read fresh on every cycle so a change on <c>/settings</c> does not wait for a restart.
/// </summary>
public sealed record PollingSettings(string Channel, bool CheckEnabled, int CheckIntervalHours);

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
    public async Task<string> GetChannelAsync(CancellationToken ct) =>
        (await GetPollingSettingsAsync(ct)).Channel;

    /// <summary>
    /// Everything the periodic check needs, from the same authoritative place the channel comes from:
    /// the database. The environment (<see cref="UpdaterOptions"/>) is only the default for a house that
    /// never saved settings — otherwise the toggle and the interval on <c>/settings</c> would be decoration.
    /// </summary>
    public async Task<PollingSettings> GetPollingSettingsAsync(CancellationToken ct)
    {
        var settings = await TryReadSettingsAsync(ct);

        return new PollingSettings(
            UpdateChannels.IsKnown(settings?.Channel) ? settings!.Channel! : await ChannelFromEnvAsync(ct),
            settings?.CheckEnabled ?? _options.CheckEnabled,
            Math.Clamp(settings?.CheckIntervalHours ?? _options.CheckIntervalHours, 1, 24 * 7));
    }

    /// <summary>
    /// Stamps the outcome of a background check so the UI can show when the house last looked and how it
    /// went. Best-effort: a failure to record must never turn into a failed check.
    /// </summary>
    public async Task RecordCheckResultAsync(string result, CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(nameof(UpdateCatalog));
            var response = await http.PostAsync(
                $"{_options.DbGatewayBaseUrl.TrimEnd('/')}/api/update-settings/check-result?result={result}",
                null, ct);

            if (!response.IsSuccessStatusCode)
                _logger.LogDebug("Отметка о проверке не записана: {Status}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Отметка о проверке не записана");
        }
    }

    private async Task<UpdateSettingsDto?> TryReadSettingsAsync(CancellationToken ct)
    {
        try
        {
            var http = _httpFactory.CreateClient(nameof(UpdateCatalog));
            return await http.GetFromJsonAsync<UpdateSettingsDto>(
                $"{_options.DbGatewayBaseUrl.TrimEnd('/')}/api/update-settings", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Настройки обновлений не получены от db-gateway — используем .env и окружение");
            return null;
        }
    }

    private async Task<string> ChannelFromEnvAsync(CancellationToken ct)
    {
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

            if (versions.Count > 0)
            {
                available[component] = versions;
            }
            else
            {
                // Дыра в канале — не мелочь: решатель просто не увидит компонент и откажет фразой
                // про «интерфейс, которого никто не предоставляет», не назвав настоящей причины.
                // Ровно так пропал бандл топологии, публиковавшийся под тегом вне формата версий.
                _logger.LogWarning(
                    "В канале '{Channel}' нет ни одной пригодной версии компонента '{Component}' — " +
                    "план обновления может оказаться неполным", channel, component);
            }
        }

        return new UpdateCatalogSnapshot(channel, installed, available);
    }

    private sealed record UpdateSettingsDto
    {
        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("checkEnabled")]
        public bool? CheckEnabled { get; init; }

        [JsonPropertyName("checkIntervalHours")]
        public int? CheckIntervalHours { get; init; }
    }
}
