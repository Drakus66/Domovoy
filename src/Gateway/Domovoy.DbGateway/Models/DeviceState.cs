using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class DeviceState
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StateId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string DeviceId { get; set; } = null!;
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [BsonElement("State")]
    public Dictionary<string, object> State { get; set; } = new();
    
    public string Source { get; set; } = null!;
}
