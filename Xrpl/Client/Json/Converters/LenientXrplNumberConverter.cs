using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.BinaryCodec.Numbers;

namespace Xrpl.Client.Json.Converters
{
    /// <summary>
    /// Reads an XRPL Number field of a model that comes from a node - a ledger entry or a transaction
    /// response - so that a value this version cannot read costs that field, not the object around it.
    /// </summary>
    /// <remarks>
    /// A node newer than this SDK may write a Number this version refuses (a wider mantissa, say). The
    /// strict <see cref="XrplNumberJsonConverter"/> would fail the whole entry, and with it an
    /// <c>account_objects</c> page or a transaction. Here the field reads as <c>null</c> instead; the
    /// node's text is still in the response's <c>Raw</c>. Writing is the same as the strict converter.
    /// </remarks>
    public sealed class LenientXrplNumberConverter : JsonConverter<XrplNumber?>
    {
        private static readonly XrplNumberJsonConverter Strict = new XrplNumberJsonConverter();

        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <inheritdoc />
        public override XrplNumber? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
                return null;
            }

            try
            {
                return Strict.Read(ref reader, typeof(XrplNumber), options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, XrplNumber? value, JsonSerializerOptions options)
        {
            if (value is { } number)
                Strict.Write(writer, number, options);
            else
                writer.WriteNullValue();
        }
    }
}
