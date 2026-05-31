using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// First-class area/zone of the home and grounds (roadmap P0-3). Zones form a graph via
/// <see cref="ParentZoneId"/> — e.g. floor → room indoors, or plot → bed/lawn/gate outdoors.
/// Devices reference a zone through <see cref="CapabilityDeviceDocument.ZoneId"/>; automations,
/// climate-by-zone and the UI grouping all build on this. Persisted as plain BSON types in the
/// <c>zones</c> collection (no contract dependency — zone assignment is a read-model concern).
/// </summary>
public class Zone
{
    /// <summary>Stable zone id (GUID as string). Generated on create when not supplied.</summary>
    [BsonId]
    public string Id { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Parent zone in the area graph; null for a top-level zone (floor / outdoor plot).</summary>
    public string? ParentZoneId { get; set; }

    /// <summary>
    /// Free-form classification used for icons/ordering, e.g. "floor", "room", "outdoor", "bed",
    /// "lawn", "gate". Not a closed enum — the domain stays open like the capability model.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>Optional explicit sort order within the parent; lower comes first.</summary>
    public int Order { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
