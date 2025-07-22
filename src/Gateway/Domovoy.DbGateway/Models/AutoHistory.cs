using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class AutoHistory
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string HistoryId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string AutoId { get; set; } = null!;
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public bool Triggered { get; set; }
    
    public string Result { get; set; } = null!;
}
