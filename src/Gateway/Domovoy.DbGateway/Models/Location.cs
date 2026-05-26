using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class Location
{
    [BsonId]
    public string LocationId { get; set; } = null!;

    public string Name { get; set; } = null!;
    
    public string? Description { get; set; }
    
    public string? ParentLocationId { get; set; }
    
    public int? Floor { get; set; }
    
    [BsonElement("Map")]
    public Dictionary<string, object>? Map { get; set; }
}
