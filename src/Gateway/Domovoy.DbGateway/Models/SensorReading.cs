using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class SensorReading
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ReadingId { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string SensorId { get; set; } = null!;
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public double Value { get; set; }
    
    public string Unit { get; set; } = null!;
}
