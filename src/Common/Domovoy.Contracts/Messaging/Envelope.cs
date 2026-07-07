// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using System.Text.Json.Serialization;

namespace Domovoy.Contracts.Messaging;

/// <summary>
/// CloudEvents-aligned envelope shared by every message on the bus. Carrying a stable, versioned
/// <see cref="Type"/> lets services, plugins and ML bind to the contract (not to internal classes)
/// and lets consumers route/version without inspecting the payload.
/// Field names follow the CloudEvents 1.0 spec.
/// </summary>
public abstract class Envelope
{
    /// <summary>Unique id of this message occurrence.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>CloudEvents spec version.</summary>
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = "1.0";

    /// <summary>Versioned event/command type, e.g. <c>"domovoy.device.state.v1"</c> — see <see cref="MessageTypes"/>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Producer of the message, e.g. <c>"connectivity/zigbee2mqtt"</c>.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>Subject the message is about — typically the device id or zone id.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>When the occurrence happened (UTC).</summary>
    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Correlates a command with the event(s) it produced.</summary>
    [JsonPropertyName("correlationid")]
    public string? CorrelationId { get; init; }
}

/// <summary>Strongly-typed envelope carrying a contract payload of type <typeparamref name="T"/>.</summary>
public sealed class Envelope<T> : Envelope
{
    [JsonPropertyName("data")]
    public required T Data { get; init; }

    /// <summary>Convenience factory that stamps the matching message <paramref name="type"/>.</summary>
    public static Envelope<T> Create(string type, string source, T data, string? subject = null, string? correlationId = null) =>
        new() { Type = type, Source = source, Data = data, Subject = subject, CorrelationId = correlationId };
}
