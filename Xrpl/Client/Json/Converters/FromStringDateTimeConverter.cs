using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters;

public class FromStringDateTimeConverter : JsonConverter<DateTime?>
{
    private static DateTime RippleStartTime = new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                {
                    string dateTimeString = reader.GetString();
                    // Попробуем разобрать строку в DateTime
                    if (DateTime.TryParseExact(dateTimeString, "yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime dateTime))
                    {
                        return dateTime;
                    }

                    return null;
                }
            case JsonTokenType.Number:
                {
                    double totalSeconds;

                    try
                    {
                        totalSeconds = reader.GetDouble();
                    }
                    catch
                    {
                        throw new JsonException("Invalid double value.");
                    }

                    return RippleStartTime.AddSeconds(totalSeconds);
                }
            default:
                throw new JsonException($"Invalid token {reader.TokenType}. Expected string or number");
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is DateTime dateTime)
        {
            // Записываем в формате ISO 8601
            writer.WriteStringValue(dateTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
