using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// generic object json converter
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class GenericStringConverter<T> : JsonConverter<T>
    {
        /// <inheritdoc />
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
                for (int i = innerOptions.Converters.Count - 1; i >= 0; i--)
                {
                    if (innerOptions.Converters[i] is GenericStringConverter<T>)
                        innerOptions.Converters.RemoveAt(i);
                }
                return JsonSerializer.Deserialize<T>(ref reader, innerOptions);
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                using JsonDocument doc = JsonDocument.ParseValue(ref reader);
                string raw = doc.RootElement.GetRawText();
                return JsonSerializer.Deserialize<T>(raw, options);
            }

            string str = reader.GetString();
            return JsonSerializer.Deserialize<T>(str, options);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
