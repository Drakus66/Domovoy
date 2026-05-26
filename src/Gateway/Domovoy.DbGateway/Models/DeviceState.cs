using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class DeviceState
{
    [BsonId]
    public string StateId { get; set; } = null!;

    public string DeviceId { get; set; } = null!;
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [BsonElement("State")]
    public Dictionary<string, object> State { get; set; } = new();
    
    public string Source { get; set; } = null!;
}
