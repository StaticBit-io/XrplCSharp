using System;

using Newtonsoft.Json;

namespace Xrpl.Client.Json.Converters;

public sealed class UInt64HexJsonConverter : JsonConverter<ulong>
{
    public override ulong ReadJson(
        JsonReader reader,
        Type objectType,
        ulong existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String)
            return Convert.ToUInt64((string)reader.Value!, 16);

        if (reader.TokenType == JsonToken.Integer)
            return Convert.ToUInt64(reader.Value);

        throw new JsonSerializationException("Invalid UInt64 hex value");
    }

    public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer)
    {
        writer.WriteValue(value.ToString("X"));
    }
}