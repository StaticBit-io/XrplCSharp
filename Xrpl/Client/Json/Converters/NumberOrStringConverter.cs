using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Reads a JSON value that may be either a number or a string and always returns it as a <see cref="string"/>.
    /// Useful for fields like <c>ledger_index</c> which some API methods return as a quoted integer
    /// and others return as a native JSON number.
    /// </summary>
    public class NumberOrStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();

                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long longValue))
                        return longValue.ToString();
                    if (reader.TryGetDecimal(out decimal decimalValue))
                        return decimalValue.ToString();
                    return reader.GetDouble().ToString();

                case JsonTokenType.Null:
                    return null;

                default:
                    throw new JsonException($"Cannot convert {reader.TokenType} to string.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }
}
