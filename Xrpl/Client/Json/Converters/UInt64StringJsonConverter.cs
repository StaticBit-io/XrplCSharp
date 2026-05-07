using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters;

public sealed class UInt64StringJsonConverter : JsonConverter<ulong>
{
    private const ulong XRPL_MAX = long.MaxValue; // 2^63 - 1

    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString();
            if (ulong.TryParse(str, out ulong value) && value <= XRPL_MAX)
                return value;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            ulong value = reader.GetUInt64();
            if (value <= XRPL_MAX)
                return value;
        }

        throw new JsonException("Invalid UInt64 XRPL value");
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        if (value > XRPL_MAX)
            throw new JsonException("XRPL UInt64 overflow");

        writer.WriteStringValue(value.ToString());
    }
}
