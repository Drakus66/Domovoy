using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class Device
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string DeviceId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string LocationId { get; set; } = null!;

    public string Status { get; set; } = "Offline";

    [BsonElement("Config")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public bool IsOnline { get; set; }
}
