using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class Light
{
    [BsonId]
    public string LightId { get; set; } = null!;

    public string DeviceId { get; set; } = null!;
    
    public bool Dimmable { get; set; }
    
    public bool ColorSupport { get; set; }
    
    public int DefaultBrightness { get; set; } = 100;
}
