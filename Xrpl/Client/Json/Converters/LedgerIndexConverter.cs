using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// <see cref="LedgerIndex"/> json converter
    /// </summary>
    public class LedgerIndexConverter : JsonConverter<LedgerIndex>
    {
        /// <summary>
        /// write <see cref="LedgerIndex"/>  to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"><see cref="LedgerIndex"/> object</param>
        /// <param name="options">json serializer options</param>
        /// <exception cref="JsonException">Cannot write this object type</exception>
        public override void Write(Utf8JsonWriter writer, LedgerIndex value, JsonSerializerOptions options)
        {
            if (value.Index.HasValue)
            {
                writer.WriteNumberValue(value.Index.Value);
            }
            else
            {
                writer.WriteStringValue(value.LedgerIndexType.ToString().ToLower());
            }
        }

        /// <summary> read <see cref="LedgerIndex"/> from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="typeToConvert">target type</param>
        /// <param name="options">json serializer options</param>
        /// <returns><see cref="LedgerIndex"/> </returns>
        /// <exception cref="JsonException">Cannot convert value</exception>
        public override LedgerIndex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return new LedgerIndex(reader.GetUInt32());
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string str = reader.GetString();

                // The number goes first: Enum.TryParse also accepts any integer string and returns
                // it cast to the enum, so "12345" became an undefined LedgerIndexType instead of a
                // ledger index.
                if (uint.TryParse(str, NumberStyles.None, CultureInfo.InvariantCulture, out uint index))
                {
                    return new LedgerIndex(index);
                }

                if (Enum.TryParse<LedgerIndexType>(str, ignoreCase: true, out LedgerIndexType indexType)
                    && Enum.IsDefined(typeof(LedgerIndexType), indexType))
                {
                    return new LedgerIndex(indexType);
                }
            }

            throw new JsonException($"Cannot convert token {reader.TokenType} to LedgerIndex");
        }
    }
}
