using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> Ripple datetime converter </summary>
    public class RippleDateTimeConverter : JsonConverter<DateTime?>
    {
        /// <summary> ripple start time </summary>
        private static DateTime RippleStartTime = new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// write  <see cref="DateTime"/>  to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"> <see cref="DateTime"/> value</param>
        /// <param name="options">json serializer options</param>
        /// <exception cref="ArgumentException">value  provided is not a DateTime</exception>
        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is DateTime dateTime)
            {
                long totalSeconds = (long)(dateTime - RippleStartTime).TotalSeconds;
                writer.WriteNumberValue(totalSeconds);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        /// <summary> read  <see cref="DateTime"/>  from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="typeToConvert">target type</param>
        /// <param name="options">json serializer options</param>
        /// <returns><see cref="DateTime"/></returns>
        /// <exception cref="JsonException">Invalid double value. or Invalid token. Expected string</exception>
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null: return null;
                case JsonTokenType.String:
                    {
                        string str = reader.GetString();
                        if (DateTime.TryParse(str, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                            return parsed;
                        double totalSeconds = Convert.ToDouble(str, CultureInfo.InvariantCulture);
                        return RippleStartTime.AddSeconds(totalSeconds);
                    }
                case JsonTokenType.Number:
                    {
                        double totalSeconds = reader.GetDouble();
                        return RippleStartTime.AddSeconds(totalSeconds);
                    }
                default: throw new JsonException("Invalid token. Expected string or number");
            }
        }
    }
}
