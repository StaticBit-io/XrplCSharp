using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters;

/// <summary>
/// Converts a DateTime to and from a Unix timestamp (seconds since the Unix Epoch).<br/> 
/// </summary>
public class UnixDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is DateTime dateTime)
        {
            long totalSeconds = (long)(dateTime - UnixEpoch).TotalSeconds;
            writer.WriteNumberValue(totalSeconds);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return null;
            case JsonTokenType.String:
            {
                string str = reader.GetString();
                double totalSeconds = Convert.ToDouble(str, CultureInfo.InvariantCulture);
                return UnixEpoch.AddSeconds(totalSeconds);
            }
            case JsonTokenType.Number:
            {
                double totalSeconds = reader.GetDouble();
                return UnixEpoch.AddSeconds(totalSeconds);
            }
            default: throw new JsonException("Invalid token. Expected string or number");
        }
    }
}
