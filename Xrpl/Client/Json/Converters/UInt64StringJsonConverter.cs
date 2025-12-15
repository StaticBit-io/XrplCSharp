using Newtonsoft.Json;

using System;

namespace Xrpl.Client.Json.Converters;

public sealed class UInt64StringJsonConverter : JsonConverter<ulong>
{
    private const ulong XRPL_MAX = long.MaxValue; // 2^63 - 1

    public override ulong ReadJson(
        JsonReader reader,
        Type objectType,
        ulong existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String)
        {
            var str = (string?)reader.Value;
            if (ulong.TryParse(str, out var value) && value <= XRPL_MAX)
                return value;
        }
        else if (reader.TokenType == JsonToken.Integer)
        {
            var value = Convert.ToUInt64(reader.Value);
            if (value <= XRPL_MAX)
                return value;
        }

        throw new JsonSerializationException("Invalid UInt64 XRPL value");
    }

    public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer)
    {
        if (value > XRPL_MAX)
            throw new JsonSerializationException("XRPL UInt64 overflow");

        writer.WriteValue(value.ToString());
    }
}