using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System.Text.Json;

namespace Domovoy.DbGateway.Serializers;

using MongoDB.Bson;

/// <summary>
/// Custom MongoDB serializer for System.Text.Json.JsonElement
/// </summary>
public class JsonElementSerializer : SerializerBase<JsonElement>
{
    public override JsonElement Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.CurrentBsonType;
        
        switch (bsonType)
        {
            case BsonType.String:
                var jsonString = context.Reader.ReadString();
                return JsonDocument.Parse(jsonString).RootElement;
            case BsonType.Document:
                var document = BsonDocumentSerializer.Instance.Deserialize(context, args);
                var json = document.ToJson();
                return JsonDocument.Parse(json).RootElement;
            case BsonType.Null:
                context.Reader.ReadNull();
                return default;
            default:
                throw new NotSupportedException($"Cannot deserialize JsonElement from BsonType {bsonType}");
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            context.Writer.WriteNull();
            return;
        }

        var jsonString = value.GetRawText();
        var document = BsonDocument.Parse(jsonString);
        BsonDocumentSerializer.Instance.Serialize(context, args, document);
    }
}
