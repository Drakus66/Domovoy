using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class Device
{
    [BsonId]
    public string DeviceId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string LocationId { get; set; } = null!;

    public string Status { get; set; } = "Offline";

    [BsonElement("Config")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public bool IsOnline { get; set; }
}
