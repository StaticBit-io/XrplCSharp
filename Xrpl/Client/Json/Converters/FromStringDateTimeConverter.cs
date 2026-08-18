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
                    // "K" accepts both the "Z" suffix rippled actually sends for close_time_iso
                    // (e.g. "2013-03-12T23:16:50Z") and a numeric offset ("+02:00"); "zzz" alone
                    // only accepts the latter, so every real "Z"-suffixed timestamp silently failed
                    // to parse and this converter returned null for it. AdjustToUniversal converts
                    // a numeric-offset value to UTC instead of leaving it in local time; AssumeUniversal
                    // still covers the (protocol-invalid) case of no zone marker at all, unchanged
                    // from before.
                    if (DateTime.TryParseExact(
                            dateTimeString,
                            "yyyy-MM-ddTHH:mm:ssK",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out DateTime dateTime))
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
            // "K" mirrors the Read side: a UTC-kind value (the only kind Read ever produces)
            // writes back with the "Z" suffix rippled itself sends, instead of "+00:00".
            writer.WriteStringValue(dateTime.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
