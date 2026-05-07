using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Handles decimal values that may arrive in scientific notation (e.g. 1e-05).
    /// System.Text.Json does not natively parse scientific notation into decimal,
    /// so this converter reads the raw value and uses decimal.Parse with appropriate styles.
    /// </summary>
    public class ScientificDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetDecimal(out decimal value))
                    return value;

                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                return decimal.Parse(
                    document.RootElement.GetRawText(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture);
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string raw = reader.GetString()!;
                return decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to decimal.");
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
}
