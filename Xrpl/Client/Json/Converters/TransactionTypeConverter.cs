using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Converts <see cref="TransactionType"/> enum to/from JSON strings.
    /// Unknown enum values are deserialized as <see cref="TransactionType.Unknown"/>
    /// instead of throwing, preserving the previous behavior where
    /// <c>serializer.Populate()</c> tolerated unrecognized enum values.
    /// </summary>
    public class TransactionTypeConverter : JsonConverter<TransactionType>
    {
        public override TransactionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string value = reader.GetString()!;
                if (Enum.TryParse<TransactionType>(value, ignoreCase: true, out TransactionType result))
                    return result;

                return TransactionType.Unknown;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                if (Enum.IsDefined(typeof(TransactionType), intValue))
                    return (TransactionType)intValue;

                return TransactionType.Unknown;
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to TransactionType.");
        }

        public override void Write(Utf8JsonWriter writer, TransactionType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
