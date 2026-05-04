using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Converts <see cref="Dictionary{String, Object}"/> using primitive CLR types
    /// instead of <see cref="JsonElement"/>.
    /// System.Text.Json by default deserializes <c>object</c> values as <see cref="JsonElement"/>,
    /// which breaks code that expects <c>string</c>, <c>int</c>, or nested dictionaries.
    /// XrplBinaryCodec and wallet signing rely on primitive CLR types in dictionaries.
    /// </summary>
    public sealed class DictionaryObjectConverter : JsonConverter<Dictionary<string, object>>
    {
        public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected StartObject token");

            Dictionary<string, object> dict = new Dictionary<string, object>(StringComparer.Ordinal);

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return dict;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected PropertyName token");

                string key = reader.GetString();
                reader.Read();
                dict[key] = ReadValue(ref reader);
            }

            throw new JsonException("Unexpected end of JSON");
        }

        private static object ReadValue(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();

                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out int intVal))
                        return intVal;
                    if (reader.TryGetInt64(out long longVal))
                        return longVal;
                    if (reader.TryGetUInt64(out ulong ulongVal))
                        return ulongVal;
                    return reader.GetDouble();

                case JsonTokenType.True:
                    return true;

                case JsonTokenType.False:
                    return false;

                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.StartObject:
                    Dictionary<string, object> nested = new Dictionary<string, object>(StringComparer.Ordinal);
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndObject)
                            return nested;
                        string key = reader.GetString();
                        reader.Read();
                        nested[key] = ReadValue(ref reader);
                    }
                    throw new JsonException("Unexpected end of JSON in nested object");

                case JsonTokenType.StartArray:
                    List<object> list = new List<object>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                            return list;
                        list.Add(ReadValue(ref reader));
                    }
                    throw new JsonException("Unexpected end of JSON in array");

                default:
                    throw new JsonException($"Unexpected token type: {reader.TokenType}");
            }
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, object> kvp in value)
            {
                writer.WritePropertyName(kvp.Key);
                WriteValue(writer, kvp.Value, options);
            }

            writer.WriteEndObject();
        }

        private static void WriteValue(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else if (value is string s)
            {
                writer.WriteStringValue(s);
            }
            else if (value is int i)
            {
                writer.WriteNumberValue(i);
            }
            else if (value is long l)
            {
                writer.WriteNumberValue(l);
            }
            else if (value is uint u)
            {
                writer.WriteNumberValue(u);
            }
            else if (value is ulong ul)
            {
                writer.WriteNumberValue(ul);
            }
            else if (value is double d)
            {
                writer.WriteNumberValue(d);
            }
            else if (value is decimal dec)
            {
                writer.WriteNumberValue(dec);
            }
            else if (value is bool b)
            {
                writer.WriteBooleanValue(b);
            }
            else if (value is Dictionary<string, object> dict)
            {
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object> kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteValue(writer, kvp.Value, options);
                }
                writer.WriteEndObject();
            }
            else if (value is List<object> list)
            {
                writer.WriteStartArray();
                foreach (object item in list)
                    WriteValue(writer, item, options);
                writer.WriteEndArray();
            }
            else if (value is JsonElement je)
            {
                je.WriteTo(writer);
            }
            else if (value is Guid guid)
            {
                writer.WriteStringValue(guid.ToString());
            }
            else
            {
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }
}
