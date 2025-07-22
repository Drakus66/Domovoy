using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class DeviceCommand
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommandId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string DeviceId { get; set; } = null!;
    
    public string Type { get; set; } = null!;
    
    [BsonElement("Params")]
    public Dictionary<string, object> Params { get; set; } = new();
    
    public string Status { get; set; } = "Pending";
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
