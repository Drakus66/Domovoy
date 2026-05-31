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

    public static async Task EnsureCollectionsAsync(IMongoDatabase database, ILogger logger, CancellationToken ct = default)
    {
        var existing = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);

        foreach (var name in new[] { DeviceEventsCollection, SensorReadingsCollection })
        {
            if (existing.Contains(name))
            {
                logger.LogInformation("Time-series collection {Collection} already present", name);
                continue;
            }

            try
            {
                await database.CreateCollectionAsync(name, new CreateCollectionOptions
                {
                    TimeSeriesOptions = new TimeSeriesOptions(TimeField, MetaField, TimeSeriesGranularity.Seconds),
                }, ct);
                logger.LogInformation("Created time-series collection {Collection}", name);
            }
            catch (MongoCommandException ex) when (ex.Message.Contains("already exists"))
            {
                // Raced with another instance — fine.
                logger.LogInformation("Time-series collection {Collection} created concurrently", name);
            }
        }
    }
}
