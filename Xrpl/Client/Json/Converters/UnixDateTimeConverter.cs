using System;
using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Xrpl.Client.Json.Converters;

/// <summary>
/// Converts a DateTime to and from a Unix timestamp (seconds since the Unix Epoch).<br/> 
/// </summary>
public class UnixDateTimeConverter : DateTimeConverterBase
{
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is DateTime dateTime)
        {
            long totalSeconds = (long)(dateTime - UnixEpoch).TotalSeconds;
            writer.WriteValue(totalSeconds);
        }
        else
        {
            throw new ArgumentException("value provided is not a DateTime", "value");
        }
    }
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null: return null;
            case JsonToken.String or JsonToken.Integer:
            {
                double totalSeconds;
                try
                {
                    totalSeconds = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    throw new Exception("Invalid double value.");
                }
                return UnixEpoch.AddSeconds(totalSeconds);
            }
            default: throw new Exception("Invalid token. Expected string");
        }
    }
}