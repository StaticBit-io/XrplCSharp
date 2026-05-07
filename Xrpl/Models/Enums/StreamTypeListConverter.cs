using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Models.Enums;
public class StreamTypeListConverter : JsonConverter<List<StreamType>>
{
    public override void Write(Utf8JsonWriter writer, List<StreamType> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (value != null)
        {
            foreach (StreamType item in value)
            {
                EnumMemberAttribute enumMember = item.GetType()
                    .GetMember(item.ToString()!)
                    .FirstOrDefault()?
                    .GetCustomAttribute<EnumMemberAttribute>();

                writer.WriteStringValue(enumMember?.Value ?? item.ToString());
            }
        }
        writer.WriteEndArray();
    }

    public override List<StreamType> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        List<StreamType> result = new List<StreamType>();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array for StreamType list");

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
                continue;

            string stringValue = reader.GetString();

            FieldInfo match = typeof(StreamType)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(f =>
                    f.GetCustomAttribute<EnumMemberAttribute>()?.Value == stringValue
                    || f.Name.Equals(stringValue, StringComparison.OrdinalIgnoreCase)
                );

            if (match != null)
                result.Add((StreamType)match.GetValue(null)!);
        }

        return result;
    }
}
