using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Models.Transactions;

namespace Xrpl.Client.Json.Converters
{
    /// <summary> <see cref="Meta"/> json converter </summary>
    public class MetaBinaryConverter : JsonConverter<Meta>
    {
        /// <summary>
        /// write <see cref="Meta"/> to json object
        /// </summary>
        /// <param name="writer">writer</param>
        /// <param name="value"><see cref="Meta"/> value</param>
        /// <param name="options">json serializer options</param>
        public override void Write(Utf8JsonWriter writer, Meta value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // Remove this converter from options to avoid infinite recursion
            JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
            innerOptions.Converters.Remove(this);
            JsonSerializer.Serialize(writer, value, innerOptions);
        }

        /// <summary> read  <see cref="Meta"/> from json object </summary>
        /// <param name="reader">json reader</param>
        /// <param name="typeToConvert">target type</param>
        /// <param name="options">json serializer options</param>
        /// <returns> <see cref="Meta"/></returns>
        public override Meta Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                return new Meta { MetaBlob = reader.GetString() };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Remove this converter from options to avoid infinite recursion
                JsonSerializerOptions innerOptions = new JsonSerializerOptions(options);
                innerOptions.Converters.Remove(this);
                return JsonSerializer.Deserialize<Meta>(ref reader, innerOptions);
            }

            throw new JsonException($"Cannot convert token {reader.TokenType} to Meta");
        }
    }
}
