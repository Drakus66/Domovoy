// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2025-2026 Ilya Dryagin
// This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.

using MongoDB.Bson;
using MongoDB.Driver;

namespace Domovoy.DbGateway.Services;

/// <summary>
/// Ensures the append-only feature-store collections exist as MongoDB <b>time-series</b> collections
/// (roadmap P0-5). Time-series collections must be created explicitly (an implicit insert would make
/// a plain collection), so we create them on startup if missing. Idempotent: existing collections are
/// left untouched. Single Mongo database — no separate TSDB (owner decision).
/// </summary>
public static class TimeSeriesInitializer
{
    /// <summary>Domain event-log: every device/user/rule action with state delta + trigger.</summary>
    public const string DeviceEventsCollection = "device_events";

    /// <summary>Numeric telemetry samples (temperature, humidity, co2, power, …).</summary>
    public const string SensorReadingsCollection = "sensor_readings";

    private const string TimeField = "Timestamp";
    private const string MetaField = "Meta";

    public static async Task EnsureCollectionsAsync(
        IMongoDatabase database, ILogger logger, int rawRetentionDays = 0, CancellationToken ct = default)
    {
        var existing = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);

        foreach (var name in new[] { DeviceEventsCollection, SensorReadingsCollection })
        {
            // Retention (TTL) applies to raw telemetry only; the event-log is the replay/ML store (Epic 1B).
            var ttl = name == SensorReadingsCollection && rawRetentionDays > 0
                ? TimeSpan.FromDays(rawRetentionDays)
                : (TimeSpan?)null;

            if (existing.Contains(name))
            {
                logger.LogInformation("Time-series collection {Collection} already present", name);
            }
            else
            {
                try
                {
                    await database.CreateCollectionAsync(name, new CreateCollectionOptions
                    {
                        TimeSeriesOptions = new TimeSeriesOptions(TimeField, MetaField, TimeSeriesGranularity.Seconds),
                        ExpireAfter = ttl,
                    }, ct);
                    logger.LogInformation("Created time-series collection {Collection}{Ttl}", name,
                        ttl is null ? "" : $" (retention {rawRetentionDays}d)");
                }
                catch (MongoCommandException ex) when (ex.Message.Contains("already exists"))
                {
                    // Raced with another instance — fine.
                    logger.LogInformation("Time-series collection {Collection} created concurrently", name);
                }
            }

            // Apply/clear the retention policy on every startup so a config change takes effect (Epic 1B).
            if (name == SensorReadingsCollection)
                await ApplyRetention(database, name, rawRetentionDays, logger, ct);
        }

        await EnsureIndexesAsync(database, logger, ct);
    }

    /// <summary>
    /// Secondary indexes matching the query shapes of <c>/api/events</c>, <c>/api/telemetry</c> and the
    /// automation-history endpoints — without them every filtered read is a collection scan. Idempotent
    /// (CreateMany on an existing identical index is a no-op); requires MongoDB 6.3+ for non-meta fields
    /// on time-series collections, so failures only log a warning.
    /// </summary>
    private static async Task EnsureIndexesAsync(IMongoDatabase database, ILogger logger, CancellationToken ct)
    {
        var events = database.GetCollection<BsonDocument>(DeviceEventsCollection);
        await CreateIndexes(events, logger, ct,
            ("Meta.DeviceId", "Timestamp"),
            ("Meta.ZoneId", "Timestamp"),
            ("CapabilityId", "Timestamp"));

        var readings = database.GetCollection<BsonDocument>(SensorReadingsCollection);
        await CreateIndexes(readings, logger, ct,
            ("Meta.DeviceId", "Timestamp"),
            ("Meta.ZoneId", "Timestamp"),
            ("Meta.CapabilityId", "Timestamp"));

        var history = database.GetCollection<BsonDocument>(Endpoints.AutomationEndpoints.HistoryCollection);
        await CreateIndexes(history, logger, ct,
            ("RuleId", "Timestamp"));
        await CreateIndexes(history, logger, ct, (null, "Timestamp"));

        // Control-block run history (Epic 1H) — the 'block' Activity source queries by time window.
        var blockHistory = database.GetCollection<BsonDocument>(Models.BlockHistory.Collection);
        await CreateIndexes(blockHistory, logger, ct,
            ("BlockId", "Timestamp"));
        await CreateIndexes(blockHistory, logger, ct, (null, "Timestamp"));
    }

    private static async Task CreateIndexes(
        IMongoCollection<BsonDocument> collection, ILogger logger, CancellationToken ct,
        params (string? Eq, string Time)[] shapes)
    {
        foreach (var (eq, time) in shapes)
        {
            var keys = eq is null
                ? Builders<BsonDocument>.IndexKeys.Descending(time)
                : Builders<BsonDocument>.IndexKeys.Ascending(eq).Descending(time);
            try
            {
                await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys), cancellationToken: ct);
                logger.LogInformation("Index on {Collection} ({Eq}, {Time}) ensured",
                    collection.CollectionNamespace.CollectionName, eq ?? "-", time);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create index on {Collection} ({Eq}, {Time})",
                    collection.CollectionNamespace.CollectionName, eq ?? "-", time);
            }
        }
    }

    /// <summary>Set (or clear when 0) the TTL on an existing collection via <c>collMod</c>.</summary>
    private static async Task ApplyRetention(
        IMongoDatabase database, string collection, int retentionDays, ILogger logger, CancellationToken ct)
    {
        try
        {
            var command = new BsonDocument
            {
                { "collMod", collection },
                // MongoDB accepts a number of seconds, or the string "off" to remove the TTL.
                { "expireAfterSeconds", retentionDays > 0 ? (BsonValue)(retentionDays * 86400L) : "off" },
            };
            await database.RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
            logger.LogInformation("Telemetry retention for {Collection}: {Policy}", collection,
                retentionDays > 0 ? $"{retentionDays}d" : "keep forever");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply retention policy to {Collection}", collection);
        }
    }
}
