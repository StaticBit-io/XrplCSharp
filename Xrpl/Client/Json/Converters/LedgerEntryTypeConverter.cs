using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Models;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Converts <see cref="LedgerEntryType"/> enum to/from JSON strings.
    /// Unknown enum values are deserialized as <see cref="LedgerEntryType.Unknown"/>
    /// instead of throwing, preserving forward compatibility with new ledger object types.
    /// </summary>
    public class LedgerEntryTypeConverter : JsonConverter<LedgerEntryType>
    {
        public override LedgerEntryType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string value = reader.GetString()!;
                if (Enum.TryParse<LedgerEntryType>(value, ignoreCase: true, out LedgerEntryType result))
                    return result;

                return LedgerEntryType.Unknown;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                if (Enum.IsDefined(typeof(LedgerEntryType), intValue))
                    return (LedgerEntryType)intValue;

                return LedgerEntryType.Unknown;
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to LedgerEntryType.");
        }

        public override void Write(Utf8JsonWriter writer, LedgerEntryType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
