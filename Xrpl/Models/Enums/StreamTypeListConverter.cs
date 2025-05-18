using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Xrpl.Models.Enums;
public class StreamTypeListConverter : JsonConverter<List<StreamType>>
{
    public override void WriteJson(JsonWriter writer, List<StreamType>? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        if (value != null)
        {
            foreach (var item in value)
            {
                var enumMember = item.GetType()
                    .GetMember(item.ToString()!)
                    .FirstOrDefault()?
                    .GetCustomAttribute<EnumMemberAttribute>();

                writer.WriteValue(enumMember?.Value ?? item.ToString());
            }
        }
        writer.WriteEndArray();
    }

    public override List<StreamType> ReadJson(JsonReader reader, Type objectType, List<StreamType>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var result = new List<StreamType>();

        if (reader.TokenType != JsonToken.StartArray)
            throw new JsonSerializationException("Expected array for StreamType list");

        while (reader.Read() && reader.TokenType != JsonToken.EndArray)
        {
            if (reader.TokenType != JsonToken.String)
                continue;

            var stringValue = (string?)reader.Value;

            var match = typeof(StreamType)
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

    public override bool CanRead => true;
}
