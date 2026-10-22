// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

namespace Domovoy.DbGateway.Stores;

/// <summary>
/// Domain store for the append-only feature store (roadmap Epic 3H, storage-abstraction seam Ф1): the event-log
/// (P0-5) and numeric telemetry (1B), plus their rollups/counters. The interface is expressed in <b>domain
/// operations</b> only — plain parameters in, plain records out — with <b>no</b> <c>IMongoDatabase</c>,
/// <c>FilterDefinition</c> or <c>BsonDocument</c> crossing the boundary, so a second backend (PostgreSQL, Ф2) is
/// a new implementation of this contract and nothing above it changes. <see cref="Endpoints.HistoryEndpoints"/>
/// depends only on this; the Mongo specifics (time-series collections, <c>$dateTrunc</c> rollups) live in
/// <see cref="Mongo.MongoTelemetryStore"/>. This is the reference migration; the remaining DbGateway domains
/// follow the same pattern (see <c>docs/architecture/db_abstraction_ru.md</c>).
/// </summary>
public interface ITelemetryStore
{
    /// <summary>Event-log rows (newest-first) matching the optional filters within the window.</summary>
    Task<IReadOnlyList<EventLogRow>> QueryEventsAsync(
        string? deviceId, string? capabilityId, string? zoneId, string? kind,
        DateTime? from, DateTime? to, int? limit, CancellationToken ct);

    /// <summary>Telemetry samples (newest-first) matching the optional filters within the window.</summary>
    Task<IReadOnlyList<TelemetryRow>> QueryTelemetryAsync(
        string? deviceId, string? capabilityId, string? zoneId,
        DateTime? from, DateTime? to, int? limit, CancellationToken ct);

    /// <summary>Count of telemetry samples matching the filter (ML data-sufficiency check, 2P).</summary>
    Task<long> CountTelemetryAsync(string? capabilityId, string? zoneId, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Per-zone telemetry sample counts in one round-trip.</summary>
    Task<IReadOnlyList<ZoneCount>> CountTelemetryByZoneAsync(string? capabilityId, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Count of event-log rows matching the filter.</summary>
    Task<long> CountEventsAsync(string? capabilityId, string? zoneId, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Per-zone event counts in one round-trip.</summary>
    Task<IReadOnlyList<ZoneCount>> CountEventsByZoneAsync(string? capabilityId, DateTime? from, DateTime? to, CancellationToken ct);

    /// <summary>Timestamp of the oldest recorded event ("how old is the history", 3I cold-start gate), or null.</summary>
    Task<DateTime?> EarliestEventAsync(CancellationToken ct);

    /// <summary>Minute/hour/day rollup of one telemetry series (1B). Throws <see cref="ArgumentException"/> on a
    /// bad bucket/agg (the caller maps it to 400).</summary>
    Task<IReadOnlyList<AggregateBucket>> AggregateTelemetryAsync(
        string? deviceId, string? capabilityId, string? zoneId,
        DateTime? from, DateTime? to, string? bucket, string? agg, CancellationToken ct);

    /// <summary>Rollup of many (device, capability) series in one round-trip (dashboard fill). Throws
    /// <see cref="ArgumentException"/> on a bad bucket/agg or too many series.</summary>
    Task<IReadOnlyList<SeriesResult>> AggregateBatchAsync(
        IReadOnlyList<SeriesSpec>? series, DateTime? from, DateTime? to,
        string? bucket, string? agg, int? maxPoints, CancellationToken ct);

    /// <summary>The most recent event-log row per requested device (batch provenance). Throws
    /// <see cref="ArgumentException"/> on too many devices.</summary>
    Task<IReadOnlyList<LatestEventRow>> LatestByDeviceAsync(
        IReadOnlyList<string>? deviceIds, DateTime? from, DateTime? to, CancellationToken ct);
}

// --- Domain records crossing the store boundary (no storage types) ---

/// <summary>One flattened event-log row.</summary>
public record EventLogRow(
    DateTime Timestamp, string DeviceId, string ZoneId, string Kind, string CapabilityId,
    object? OldValue, object? NewValue, string TriggerSource, string? TriggerId,
    string? RuleId, string? DecisionId, string? Mode, string? CorrelationId);

/// <summary>One flattened telemetry sample.</summary>
public record TelemetryRow(
    DateTime Timestamp, string DeviceId, string ZoneId, string CapabilityId, string? Unit, double Value);

/// <summary>One time bucket of aggregated telemetry (1B). <c>Value</c> is the requested aggregation.</summary>
public record AggregateBucket(DateTime Timestamp, double Value, double Min, double Max, double Avg, long Count);

/// <summary>One (device, capability) series requested in a batch aggregation.</summary>
public record SeriesSpec(string DeviceId, string CapabilityId);

/// <summary>Aggregated buckets for one requested series.</summary>
public record SeriesResult(string DeviceId, string CapabilityId, IReadOnlyList<AggregateBucket> Buckets);

/// <summary>The latest event-log row for one device ("who changed it last").</summary>
public record LatestEventRow(
    string DeviceId, DateTime Timestamp, string CapabilityId,
    string TriggerSource, string? TriggerId, string? RuleId, string? CorrelationId, object? NewValue);

/// <summary>Per-zone document count.</summary>
public record ZoneCount(string ZoneId, long Count);
