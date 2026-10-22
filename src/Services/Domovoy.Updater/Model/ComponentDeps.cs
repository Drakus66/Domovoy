// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domovoy.Updater.Model;

/// <summary>
/// What a component declares about its own compatibility (roadmap Epic 3K). Ships as the
/// <c>ru.domovoy.deps</c> OCI label, so it can be read straight from the registry without pulling
/// the image, and is produced by <c>build/tools/plan-build.mjs</c> from <c>build/components.json</c>.
/// <para>
/// Dependencies name an <b>interface</b>, never a service. That is what keeps the graph mostly
/// acyclic: each interface has exactly one provider, so "ML needs a newer API gateway" is expressed
/// as <c>requires: rest-api >= 7</c> and the resolver decides who that is.
/// </para>
/// </summary>
public sealed record ComponentDeps
{
    [JsonPropertyName("component")]
    public string Component { get; init; } = "";

    /// <summary>Container/compose name — how the executor addresses this component on the host.</summary>
    [JsonPropertyName("container")]
    public string Container { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.0.0";

    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    /// <summary>
    /// Bus compatibility. Null for components that are not on the bus (the WebUI): they carry no
    /// copy of <c>Domovoy.Contracts</c> and cannot constrain it.
    /// </summary>
    [JsonPropertyName("bus")]
    public BusCompatibility? Bus { get; init; }

    [JsonPropertyName("provides")]
    public Dictionary<string, ProvidedInterface> Provides { get; init; } = new();

    [JsonPropertyName("requires")]
    public Dictionary<string, int> Requires { get; init; } = new();

    /// <summary>Extra files a topology bundle carries; empty for ordinary components.</summary>
    [JsonPropertyName("assets")]
    public List<string> Assets { get; init; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parses the label. Returns null on anything malformed rather than throwing: an image with a
    /// broken label must be treated as "unknown, do not offer", never crash the update check.
    /// </summary>
    public static ComponentDeps? TryParse(string? labelValue)
    {
        if (string.IsNullOrWhiteSpace(labelValue)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ComponentDeps>(labelValue, Json);
            return string.IsNullOrWhiteSpace(parsed?.Component) ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The bus has no provider — <c>Domovoy.Contracts</c> lives inside every image — so compatibility is
/// a property of the whole set: it holds when <c>max(Understands) &lt;= min(Speaks)</c>.
/// <para>
/// This is what makes a "release train" a computed special case rather than a separate mode: a
/// breaking contract change raises <see cref="Understands"/> in the new images, and the resolver
/// concludes on its own that everything must move.
/// </para>
/// </summary>
public sealed record BusCompatibility
{
    /// <summary>Contract version this component emits.</summary>
    [JsonPropertyName("speaks")]
    public int Speaks { get; init; }

    /// <summary>Oldest contract version this component still understands.</summary>
    [JsonPropertyName("understands")]
    public int Understands { get; init; }
}

/// <summary>
/// A named HTTP interface a component serves. The supported range is
/// <c>[MinCompat, Version]</c>: a consumer requiring <c>R</c> is satisfied when
/// <c>MinCompat &lt;= R &lt;= Version</c>.
/// </summary>
public sealed record ProvidedInterface
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>Oldest consumer requirement still served. Raising it breaks older consumers.</summary>
    [JsonPropertyName("minCompat")]
    public int MinCompat { get; init; }
}
