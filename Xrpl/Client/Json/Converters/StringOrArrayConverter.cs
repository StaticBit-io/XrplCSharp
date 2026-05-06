using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> string or array json converter </summary>
    public class StringOrArrayConverter : JsonConverter<object>
    {
        /// <summary>
        /// write  string or array to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"> string or array value</param>
        /// <param name="options">json serializer options</param>
        /// <exception cref="JsonException">Cannot write value</exception>
        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;

                case string s:
                    writer.WriteStringValue(s);
                    break;

                case List<string> list:
                    JsonSerializer.Serialize(writer, list, options);
                    break;

                case string[] array:
                    JsonSerializer.Serialize(writer, array, options);
                    break;

                default:
                    throw new JsonException($"Cannot write value of type {value.GetType()}");
            }
        }

        /// <summary> read  string or array  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="typeToConvert">target type</param>
        /// <param name="options">json serializer options</param>
        /// <returns>string or array </returns>
        /// <exception cref="JsonException">Cannot convert value</exception>
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.StartArray => JsonSerializer.Deserialize<List<string>>(ref reader, options),
                _ => throw new JsonException($"Cannot convert token {reader.TokenType} to {typeToConvert}")
            };
        }

        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert == typeof(object) || typeToConvert == typeof(string) || typeToConvert == typeof(List<string>) || typeToConvert == typeof(Array) || typeToConvert == typeof(string[]);
    }
}
