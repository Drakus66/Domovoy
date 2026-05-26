using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Domovoy.DbGateway.Models;

public class Sensor
{
    [BsonId]
    public string SensorId { get; set; } = null!;

    public string DeviceId { get; set; } = null!;
    
    public string Type { get; set; } = null!;
    
    public int UpdateFrequency { get; set; } = 60; // seconds
    
    public double Precision { get; set; } = 0.1;
    
    public double? LastValue { get; set; }
}
