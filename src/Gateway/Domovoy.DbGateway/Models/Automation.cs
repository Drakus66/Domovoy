using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class Automation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string AutoId { get; set; } = null!;

    public string Name { get; set; } = null!;
    
    [BsonElement("Trigger")]
    public Dictionary<string, object> Trigger { get; set; } = new();
    
    [BsonElement("Condition")]
    public Dictionary<string, object>? Condition { get; set; }
    
    [BsonElement("Action")]
    public Dictionary<string, object> Action { get; set; } = new();
    
    public string Status { get; set; } = "Enabled";
    
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = null!;
}
