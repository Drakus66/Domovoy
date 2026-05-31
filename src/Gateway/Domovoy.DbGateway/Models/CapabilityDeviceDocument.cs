using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Persisted read-model of a capability device (roadmap Step 4). Written by the EventInterceptor
/// from the capability contract (DeviceDiscoveredV1 / DeviceStateReportV1) and read by the WebUI.
/// Uses plain BSON-friendly types (no contract records/interfaces) so Mongo serialization is simple.
/// </summary>
public class CapabilityDeviceDocument
{
    /// <summary>Logical device id (GUID as string) — stable across protocol renames/restarts.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    /// <summary>Owning adapter, e.g. "Zigbee2Mqtt" or "DomovoyNative".</summary>
    public string AdapterSource { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string ZoneId { get; set; } = string.Empty;

    public List<CapabilityDocument> Capabilities { get; set; } = new();

    /// <summary>Latest normalized state, keyed by capability id (on_off, brightness, temperature, …).</summary>
    public Dictionary<string, object> State { get; set; } = new();

    public bool IsOnline { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>Flattened capability descriptor for persistence/UI.</summary>
public class CapabilityDocument
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // Boolean | Number | Enum | Color | Text | Action
    public bool Writable { get; set; }
    public string? Unit { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
}
