using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.BinaryCodec.Numbers
{
    /// <summary>
    /// Reads an <see cref="XrplNumber"/> from a JSON string or number and writes it as the string
    /// rippled writes for the field. Applied to <see cref="XrplNumber"/> itself, so no registration is
    /// needed.
    /// </summary>
    public sealed class XrplNumberJsonConverter : JsonConverter<XrplNumber>
    {
        /// <inheritdoc />
        public override XrplNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string text;
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    text = reader.GetString();
                    break;
                case JsonTokenType.Number:
                    text = Encoding.UTF8.GetString(reader.HasValueSequence
                        ? reader.ValueSequence.ToArray()
                        : reader.ValueSpan.ToArray());
                    break;
                default:
                    throw new JsonException($"XrplNumber: expected a string or a number, got {reader.TokenType}.");
            }

            try
            {
                return XrplNumber.Parse(text);
            }
            catch (FormatException exception)
            {
                throw new JsonException(exception.Message, exception);
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, XrplNumber value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
