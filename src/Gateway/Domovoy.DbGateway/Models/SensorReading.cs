using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// A single numeric telemetry sample (roadmap P0-5 / Epic 1B). Written alongside the device event-log
/// whenever a numeric capability (temperature, humidity, co2, power, …) reports a value, into the
/// <c>sensor_readings</c> MongoDB <b>time-series</b> collection (timeField <see cref="Timestamp"/>,
/// metaField <see cref="Meta"/>). Feeds zone time-series charts and ML features.
/// </summary>
public class SensorReading
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>Sample time (UTC) — time-series timeField.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Low-cardinality grouping keys — time-series metaField.</summary>
    public TelemetryMeta Meta { get; set; } = new();

    public double Value { get; set; }
}

/// <summary>Time-series metaField for <see cref="SensorReading"/>.</summary>
public class TelemetryMeta
{
    public string DeviceId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string? Unit { get; set; }
}
