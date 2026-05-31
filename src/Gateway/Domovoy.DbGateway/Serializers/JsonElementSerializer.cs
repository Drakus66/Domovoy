using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using System.Text.Json;

namespace Domovoy.DbGateway.Serializers;

using MongoDB.Bson;
using MongoDB.Bson.IO;

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

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                context.Writer.WriteString(value.GetString());
                break;
            case JsonValueKind.Number:
                if (value.TryGetInt32(out int intValue))
                    context.Writer.WriteInt32(intValue);
                else if (value.TryGetInt64(out long longValue))
                    context.Writer.WriteInt64(longValue);
                else if (value.TryGetDouble(out double doubleValue))
                    context.Writer.WriteDouble(doubleValue);
                else
                    context.Writer.WriteString(value.GetRawText());
                break;
            case JsonValueKind.True:
                context.Writer.WriteBoolean(true);
                break;
            case JsonValueKind.False:
                context.Writer.WriteBoolean(false);
                break;
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                var jsonString = value.GetRawText();
                var document = BsonDocument.Parse(jsonString);
                BsonDocumentSerializer.Instance.Serialize(context, args, document);
                break;
            default:
                context.Writer.WriteString(value.GetRawText());
                break;
        }
    }
}

/// <summary>
/// Custom dictionary serializer that can handle JsonElement values
/// </summary>
public class JsonObjectDictionarySerializer : SerializerBase<Dictionary<string, object>>
{
    public JsonObjectDictionarySerializer()
    {
    }

    public override Dictionary<string, object> Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.CurrentBsonType;
        
        if (bsonType == BsonType.Null)
        {
            context.Reader.ReadNull();
            return null;
        }

        if (bsonType != BsonType.Document)
        {
            throw new NotSupportedException($"Cannot deserialize Dictionary<string, object> from BsonType {bsonType}");
        }

        var result = new Dictionary<string, object>();
        context.Reader.ReadStartDocument();

        while (context.Reader.ReadBsonType() != BsonType.EndOfDocument)
        {
            var key = context.Reader.ReadName();
            var value = DeserializeValue(context);
            result[key] = value;
        }

        context.Reader.ReadEndDocument();
        return result;
    }

    private object DeserializeValue(BsonDeserializationContext context)
    {
        var bsonType = context.Reader.CurrentBsonType;
        
        switch (bsonType)
        {
            case BsonType.String:
                return context.Reader.ReadString();
            case BsonType.Int32:
                return context.Reader.ReadInt32();
            case BsonType.Int64:
                return context.Reader.ReadInt64();
            case BsonType.Double:
                return context.Reader.ReadDouble();
            case BsonType.Boolean:
                return context.Reader.ReadBoolean();
            case BsonType.Null:
                context.Reader.ReadNull();
                return null;
            case BsonType.Document:
                var document = BsonDocumentSerializer.Instance.Deserialize(context, new BsonDeserializationArgs());
                var json = document.ToJson();
                return JsonDocument.Parse(json).RootElement;
            default:
                throw new NotSupportedException($"Cannot deserialize value from BsonType {bsonType}");
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Dictionary<string, object> value)
    {
        if (value == null)
        {
            context.Writer.WriteNull();
            return;
        }

        context.Writer.WriteStartDocument();

        foreach (var kvp in value)
        {
            context.Writer.WriteName(kvp.Key);
            
            if (kvp.Value == null)
            {
                context.Writer.WriteNull();
            }
            else if (kvp.Value is JsonElement jsonElement)
            {
                // Use the JsonElementSerializer for JsonElement values
                var jsonSerializer = new JsonElementSerializer();
                jsonSerializer.Serialize(context, args, jsonElement);
            }
            else
            {
                // Use the default object serializer for other types
                var objectSerializer = new ObjectSerializer();
                objectSerializer.Serialize(context, args, kvp.Value);
            }
        }

        context.Writer.WriteEndDocument();
    }
}
