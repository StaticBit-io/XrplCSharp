using System;
using System.Text;
using System.Text.Json;

namespace Xrpl.Client.Json
{
    /// <summary>
    /// Where a JSON token sits inside the buffer it was read from, as a byte offset and length.
    /// Carries no reference to the buffer: the envelope that owns the frame pairs the two.
    /// </summary>
    internal readonly struct JsonSlice
    {
        /// <summary>
        /// Byte offset of the first byte of the token, counted from the start of the buffer the
        /// reader was created over.
        /// </summary>
        public int Offset { get; }

        /// <summary>Length of the token in bytes.</summary>
        public int Length { get; }

        /// <summary>True when no token was recorded — the member was absent from the buffer.</summary>
        public bool IsEmpty => Length == 0;

        /// <summary>Records the token's bounds; does not copy or retain the buffer itself.</summary>
        public JsonSlice(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        /// <summary>
        /// Bounds of the sole top-level JSON value in <paramref name="buffer"/> — from its first
        /// byte to the byte after it ends, before any trailing whitespace.
        /// </summary>
        /// <remarks>
        /// A stream message is not wrapped in an envelope the way a query response is: the frame
        /// <em>is</em> the event, so there is no named member for a per-property converter like
        /// <see cref="Converters.JsonSliceConverter"/> to bind to. This computes the same bounds
        /// that converter would, for the document as a whole, so a stream event can hand out a
        /// <see cref="RawJson"/> the same way an envelope's <c>result</c> does. Returns
        /// <c>default</c> (empty) for a buffer that holds no value at all.
        /// </remarks>
        public static JsonSlice OfDocument(byte[] buffer)
        {
            Utf8JsonReader reader = new Utf8JsonReader(buffer);
            if (!reader.Read())
            {
                return default;
            }

            long start = reader.TokenStartIndex;
            reader.Skip();
            long end = reader.BytesConsumed;
            return new JsonSlice(checked((int)start), checked((int)(end - start)));
        }

        /// <summary>
        /// Bounds of the value of a top-level member named <paramref name="name"/> inside
        /// <paramref name="buffer"/>, or <c>default</c> (empty) if the object has no such member.
        /// </summary>
        /// <remarks>
        /// For a member that is one of several C# properties bound to the same JSON name via
        /// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> —
        /// <see cref="Xrpl.Models.Subscriptions.TransactionStream.Transaction"/> and its API v1
        /// alias both bind to a name the other also claims — this is the only way to record where
        /// the value sits: registering a second <see cref="Converters.JsonSliceConverter"/> member
        /// under the same name is not an option, since System.Text.Json rejects two members
        /// mapped to one JSON name outright.
        /// </remarks>
        /// <remarks>
        /// The scan does not stop at the first match: on a duplicate top-level key it keeps going
        /// to <see cref="JsonTokenType.EndObject"/> and returns the <em>last</em> one, matching
        /// <see cref="JsonSerializer"/>'s own last-value-wins behavior for a POCO property fed by a
        /// duplicate JSON member (the default unless a caller opts into
        /// <see cref="JsonSerializerOptions.AllowDuplicateProperties"/> = <see langword="false"/>,
        /// which this library's <see cref="XrplJsonOptions.Default"/> does not). Without this, a
        /// frame with two top-level <c>tx_json</c> members - not something rippled sends, but not
        /// something a proxy or a compromised link is prevented from sending either - would leave
        /// <see cref="Xrpl.Models.Subscriptions.TransactionStream.RawTransaction"/> pointing at the
        /// first occurrence while the deserializer-fed <see cref="Xrpl.Models.Subscriptions.TransactionStream.Transaction"/>
        /// reflects the last: a wallet would display one transaction and sign the other.
        /// </remarks>
        /// <remarks>
        /// Matching is case-insensitive, mirroring <see cref="XrplJsonOptions.Default"/>'s
        /// <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> = <see langword="true"/>:
        /// a frame that spells the member <c>"TX_JSON"</c> still has to populate
        /// <see cref="Xrpl.Models.Subscriptions.TransactionStream.RawTransaction"/>, because the
        /// same frame already populated the case-insensitively-matched
        /// <see cref="Xrpl.Models.Subscriptions.TransactionStream.Transaction"/> through ordinary
        /// deserialization. <see cref="Utf8JsonReader.ValueTextEquals(ReadOnlySpan{byte})"/> has no
        /// case-insensitive overload, so this decodes the property name through
        /// <see cref="Utf8JsonReader.GetString"/> (which unescapes it, same as the property-name
        /// matching System.Text.Json itself does internally) and compares with
        /// <see cref="StringComparison.OrdinalIgnoreCase"/>. See
        /// <see cref="RawJson.HasTopLevelProperty"/> for the same rule applied to presence rather
        /// than value.
        /// </remarks>
        public static JsonSlice FindTopLevelMember(byte[] buffer, ReadOnlySpan<byte> name)
        {
            Utf8JsonReader reader = new Utf8JsonReader(buffer);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return default;
            }

            string nameText = Encoding.UTF8.GetString(name);
            JsonSlice result = default;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return result;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                bool isMatch = string.Equals(reader.GetString(), nameText, StringComparison.OrdinalIgnoreCase);
                reader.Read();
                long start = reader.TokenStartIndex;
                reader.Skip();
                long end = reader.BytesConsumed;

                if (isMatch)
                {
                    result = new JsonSlice(checked((int)start), checked((int)(end - start)));
                }
            }

            return result;
        }
    }
}
