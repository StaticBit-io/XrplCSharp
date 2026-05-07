using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters;

public sealed class UInt64HexJsonConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return Convert.ToUInt64(reader.GetString()!, 16);

        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetUInt64();

        throw new JsonException("Invalid UInt64 hex value");
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("X"));
    }
}
