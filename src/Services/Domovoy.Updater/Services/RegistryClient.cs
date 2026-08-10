// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

using Domovoy.Updater.Model;

using Microsoft.Extensions.Logging;

namespace Domovoy.Updater.Services;

/// <summary>
/// Reads the image registry over the plain Docker Registry v2 API (roadmap Epic 3K).
///
/// <para><b>The trick that makes this cheap.</b> An image's version and its declared compatibility
/// live in OCI labels, and labels live in the image <i>config blob</i> — a few kilobytes, separate
/// from the layers. So checking "is there a new version, and what would it drag in" costs a manifest
/// HEAD plus one small GET per component, not a gigabyte of pulls. Nothing is downloaded until the
/// owner presses the button.</para>
///
/// <para>Packages are public, but the registry still wants a bearer token — GHCR issues an anonymous
/// one for pull scope, which is what <see cref="GetTokenAsync"/> fetches.</para>
///
/// <para>Everything downstream is addressed by <b>digest</b>, never by the moving channel tag: the tag
/// is only ever used to discover which digest it currently points at.</para>
/// </summary>
public sealed class RegistryClient
{
    // Разбираем и одиночный манифест, и индекс: buildx выпускает индекс, как только к образу
    // прикладывается attestation, поэтому рассчитывать на один формат нельзя.
    private const string AcceptManifests =
        "application/vnd.oci.image.manifest.v1+json," +
        "application/vnd.docker.distribution.manifest.v2+json," +
        "application/vnd.oci.image.index.v1+json," +
        "application/vnd.docker.distribution.manifest.list.v2+json";

    private static readonly Regex VersionTag = new(@"^\d+\.\d+\.\d+(-dev)?$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<RegistryClient> _logger;
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new();

    public RegistryClient(HttpClient http, ILogger<RegistryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Anonymous pull token for one repository. Cached until shortly before expiry.</summary>
    public async Task<string> GetTokenAsync(string repository, CancellationToken ct)
    {
        if (_tokens.TryGetValue(repository, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
            return cached.Token;

        var url = $"https://{ReleaseSource.Registry}/token" +
                  $"?scope=repository:{repository}:pull&service={ReleaseSource.Registry}";

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException($"Реестр не выдал токен для '{repository}'.");

        var lifetime = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
        _tokens[repository] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, lifetime - 30)));
        return token;
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(
        HttpMethod method, string repository, string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, $"https://{ReleaseSource.Registry}/v2/{repository}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(repository, ct));
        request.Headers.TryAddWithoutValidation("Accept", AcceptManifests);
        return request;
    }

    /// <summary>Digest a tag currently points at, or null when the tag does not exist.</summary>
    public async Task<string?> GetDigestAsync(string repository, string tag, CancellationToken ct)
    {
        using var request = await AuthorizedAsync(HttpMethod.Head, repository, $"manifests/{tag}", ct);
        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode) return null;

        if (response.Headers.TryGetValues("Docker-Content-Digest", out var values))
            return values.FirstOrDefault();

        return null;
    }

    /// <summary>Available tags, newest-first, filtered to real version tags of one channel.</summary>
    public async Task<IReadOnlyList<string>> ListVersionTagsAsync(
        string repository, string channel, CancellationToken ct)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, repository, "tags/list", ct);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<string>();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var wantDev = channel == UpdateChannels.Dev;

        return tags.EnumerateArray()
            .Select(t => t.GetString() ?? "")
            .Where(t => VersionTag.IsMatch(t) && t.EndsWith("-dev", StringComparison.Ordinal) == wantDev)
            .OrderBy(t => t, SemVerComparer.Instance)
            .Reverse()
            .ToList();
    }

    /// <summary>
    /// Image config labels for a manifest reference. Walks an index to the linux/amd64 manifest when
    /// needed, then fetches the config blob — the small object the labels actually live in.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string repository, string reference, CancellationToken ct)
    {
        var manifest = await GetJsonAsync(repository, $"manifests/{reference}", ct);
        if (manifest is null) return new Dictionary<string, string>();

        var root = manifest.RootElement;

        // Индекс: спускаемся к манифесту нашей платформы.
        if (root.TryGetProperty("manifests", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            string? platformDigest = null;
            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetProperty("platform", out var platform)) continue;
                var os = platform.TryGetProperty("os", out var o) ? o.GetString() : null;
                var arch = platform.TryGetProperty("architecture", out var a) ? a.GetString() : null;
                if (os == "linux" && arch == "amd64")
                {
                    platformDigest = child.GetProperty("digest").GetString();
                    break;
                }
            }

            manifest.Dispose();
            if (platformDigest is null) return new Dictionary<string, string>();

            manifest = await GetJsonAsync(repository, $"manifests/{platformDigest}", ct);
            if (manifest is null) return new Dictionary<string, string>();
            root = manifest.RootElement;
        }

        try
        {
            if (!root.TryGetProperty("config", out var config)) return new Dictionary<string, string>();
            var configDigest = config.GetProperty("digest").GetString();
            if (string.IsNullOrEmpty(configDigest)) return new Dictionary<string, string>();

            using var blob = await GetJsonAsync(repository, $"blobs/{configDigest}", ct);
            if (blob is null) return new Dictionary<string, string>();

            if (!blob.RootElement.TryGetProperty("config", out var imageConfig)
                || !imageConfig.TryGetProperty("Labels", out var labels)
                || labels.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, string>();

            return labels.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            manifest.Dispose();
        }
    }

    /// <summary>
    /// The single layer of a bundle image (the topology package), as a gzipped tar stream.
    /// The bundle is built <c>FROM scratch</c>, so "the layer" is unambiguous.
    /// </summary>
    public async Task<Stream> DownloadSingleLayerAsync(string repository, string reference, CancellationToken ct)
    {
        using var manifest = await GetJsonAsync(repository, $"manifests/{reference}", ct)
            ?? throw new InvalidOperationException($"Манифест '{repository}:{reference}' недоступен.");

        var root = manifest.RootElement;

        if (root.TryGetProperty("manifests", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var first = children.EnumerateArray()
                .FirstOrDefault(c => c.TryGetProperty("platform", out var p)
                                     && p.TryGetProperty("architecture", out var a)
                                     && a.GetString() == "amd64");
            var nested = first.ValueKind == JsonValueKind.Object ? first.GetProperty("digest").GetString() : null;
            if (nested is null) throw new InvalidOperationException("В индексе бандла нет манифеста linux/amd64.");
            return await DownloadSingleLayerAsync(repository, nested, ct);
        }

        var layers = root.GetProperty("layers");
        if (layers.GetArrayLength() != 1)
            throw new InvalidOperationException(
                $"Ожидался однослойный бандл, а слоёв {layers.GetArrayLength()}. " +
                "Бандл топологии собирается FROM scratch — проверьте build/topology/Dockerfile.");

        var digest = layers[0].GetProperty("digest").GetString()!;
        using var request = await AuthorizedAsync(HttpMethod.Get, repository, $"blobs/{digest}", ct);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>
    /// Versions of one component offered on a channel, newest first, each with its parsed
    /// <c>ru.domovoy.deps</c>. Capped at <paramref name="maxVersions"/>: the resolver needs room to
    /// pick an older-but-compatible build, not the entire history.
    /// </summary>
    public async Task<IReadOnlyList<AvailableComponent>> ListAvailableAsync(
        string component, string channel, int maxVersions, CancellationToken ct)
    {
        var repository = ReleaseSource.RepositoryOf(component);
        var result = new List<AvailableComponent>();

        IReadOnlyList<string> tags;
        try
        {
            tags = await ListVersionTagsAsync(repository, channel, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось получить список тегов '{Repository}'", repository);
            return result;
        }

        foreach (var tag in tags.Take(maxVersions))
        {
            try
            {
                var digest = await GetDigestAsync(repository, tag, ct);
                if (digest is null) continue;

                var labels = await GetLabelsAsync(repository, digest, ct);
                var deps = ComponentDeps.TryParse(labels.GetValueOrDefault("ru.domovoy.deps"));
                if (deps is null)
                {
                    // Образ без объявленной совместимости решателю бесполезен: предлагать его —
                    // значит обновлять вслепую. Пропускаем молча, это не ошибка установки.
                    _logger.LogDebug("У '{Repository}:{Tag}' нет метки ru.domovoy.deps — пропущен", repository, tag);
                    continue;
                }

                result.Add(new AvailableComponent(
                    Name: component,
                    Repository: repository,
                    Tag: tag,
                    Digest: digest,
                    Version: deps.Version is { Length: > 0 } v ? v : tag,
                    Deps: deps));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Пропущен тег '{Repository}:{Tag}'", repository, tag);
            }
        }

        return result;
    }

    private async Task<JsonDocument?> GetJsonAsync(string repository, string path, CancellationToken ct)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, repository, path, ct);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
