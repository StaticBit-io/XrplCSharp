using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Xrpl.Client.Json
{
    /// <summary>
    /// The bytes a node actually sent for one member of a response, as they arrived.
    /// </summary>
    /// <remarks>
    /// A window onto the frame rather than a copy of it: the frame is the exact-sized array the
    /// receive loop already allocated, so holding this costs nothing beyond keeping that array
    /// alive. UTF-16 is never stored — <see cref="ToString"/> builds it on demand, which for a
    /// large response is twice the byte length and worth paying only when something needs text.
    /// The window keeps the whole frame alive, not just the bytes it spans: for the result member
    /// that is the frame anyway, but a small window onto a large frame pins all of it. Anything
    /// outliving the response — a stored page, an entry cached across a paged crawl — should keep
    /// <see cref="ToArray"/> instead and let the frame go.
    /// </remarks>
    [DebuggerDisplay("RawJson, {Length} bytes")]
    public readonly struct RawJson : IEquatable<RawJson>
    {
        private readonly byte[]? _frame;
        private readonly int _offset;
        private readonly int _length;

        /// <summary>Records the window; does not copy the frame.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The window does not lie inside <paramref name="frame"/>.
        /// </exception>
        public RawJson(byte[]? frame, int offset, int length)
        {
            // Bounds come from JsonSliceConverter and are relative to the buffer its reader was
            // created over, not to the array behind it. Pairing a slice with a different buffer is
            // the one way this type breaks, and it breaks silently: an in-range window over the
            // wrong bytes reads back as valid JSON. Checking here fails where the pairing is made,
            // instead of as an unnamed ArgumentOutOfRangeException inside a consumer's Span read.
            if (frame is null)
            {
                if (offset != 0 || length != 0)
                {
                    throw new ArgumentNullException(nameof(frame));
                }
            }
            else if ((uint)offset > (uint)frame.Length || (uint)length > (uint)(frame.Length - offset))
            {
                // Name the argument actually at fault: an out-of-range offset leaves length blameless,
                // and reporting it as the culprit sends the reader after the wrong number.
                throw new ArgumentOutOfRangeException(
                    (uint)offset > (uint)frame.Length ? nameof(offset) : nameof(length),
                    $"Window [{offset}, {offset + (long)length}) does not lie inside a frame of {frame.Length} bytes.");
            }

            _frame = frame;
            _offset = offset;
            _length = length;
        }

        /// <summary>True when nothing was captured.</summary>
        public bool IsEmpty => _frame is null || _length == 0;

        /// <summary>Length of the captured JSON in bytes.</summary>
        public int Length => _frame is null ? 0 : _length;

        /// <summary>The captured JSON, as UTF-8, without copying.</summary>
        public ReadOnlySpan<byte> Span => _frame is null ? default : _frame.AsSpan(_offset, _length);

        /// <summary>
        /// Copies the captured JSON into a new array, detaching it from the frame. This is how a
        /// consumer keeps the bytes past the response without pinning the whole frame with them.
        /// </summary>
        public byte[] ToArray() => _frame is null ? Array.Empty<byte>() : Span.ToArray();

        /// <summary>
        /// Deserializes the captured JSON into <typeparamref name="T"/> using the library's
        /// serializer options.
        /// </summary>
        /// <remarks>
        /// Here so that a consumer does not reach for <c>JsonSerializer.Deserialize</c> with
        /// options of their own: the XRPL models depend on the converters in
        /// <see cref="XrplJsonOptions.Default"/>, and bare options silently produce a different
        /// object. Returns <c>default</c> for an empty window rather than throwing — an absent
        /// member is not a malformed one.
        /// </remarks>
        public T Deserialize<T>()
        {
            return IsEmpty ? default : JsonSerializer.Deserialize<T>(Span, XrplJsonOptions.Default);
        }

        /// <summary>
        /// Parses the captured JSON into a self-contained <see cref="JsonElement"/>.
        /// </summary>
        /// <remarks>
        /// The element copies out of the frame, so it stays readable after the frame is gone —
        /// unlike <see cref="Span"/>, which aliases it. An empty window yields
        /// <see cref="JsonValueKind.Undefined"/>.
        /// </remarks>
        public JsonElement ToJsonElement()
        {
            if (IsEmpty)
            {
                return default;
            }

            using (JsonDocument document = JsonDocument.Parse(ToArray()))
            {
                return document.RootElement.Clone();
            }
        }

        /// <summary>
        /// True when the captured JSON is an object carrying <paramref name="name"/> at its top
        /// level.
        /// </summary>
        /// <remarks>
        /// Each non-matching member's value is skipped whole, so a nested occurrence of the name
        /// cannot be mistaken for a top-level one. Matching goes through
        /// <see cref="Utf8JsonReader.ValueTextEquals(ReadOnlySpan{byte})"/>, which unescapes — a
        /// raw byte comparison would miss <c>marker</c>.
        /// </remarks>
        public bool HasTopLevelProperty(ReadOnlySpan<byte> name)
        {
            if (IsEmpty)
            {
                return false;
            }

            Utf8JsonReader reader = new Utf8JsonReader(Span);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return false;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                if (reader.ValueTextEquals(name))
                {
                    return true;
                }

                reader.Skip();
            }

            return false;
        }

        /// <summary>Writes the captured JSON into <paramref name="writer"/> verbatim.</summary>
        public void WriteTo(Utf8JsonWriter writer)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (IsEmpty)
            {
                // Covers the shape an absent member produces: the slice stays default (0, 0) and is
                // still paired with a live frame. WriteRawValue rejects an empty span outright, and
                // does so before skipInputValidation is consulted.
                writer.WriteNullValue();
                return;
            }

            // The payload was already validated as JSON when the envelope was parsed - the
            // converter reached these bounds through reader.Skip(). Re-validating here would mean
            // parsing the subtree a second time, which is the cost this type exists to remove.
            // The premise is the window's correctness, which the constructor enforces; without
            // that check a desynced window emits a malformed document with no exception at all.
            writer.WriteRawValue(Span, skipInputValidation: true);
        }

        /// <summary>
        /// Decodes the captured JSON as UTF-16 text. Allocates; call only when text is needed.
        /// This is a decode of the bytes, not the byte-exact source — invalid UTF-8 is replaced
        /// with U+FFFD. For the bytes as the node sent them, use <see cref="Span"/>.
        /// </summary>
        public override string ToString()
        {
            return _frame is null ? string.Empty : Encoding.UTF8.GetString(_frame, _offset, _length);
        }

        /// <summary>
        /// Identity, not content: two windows are equal when they address the same bytes of the
        /// same frame. Comparing the bytes themselves is what <see cref="Span"/> is for. Without
        /// this the default struct equality reflects over the fields to reach the same answer,
        /// boxing both operands, and hashes on the frame reference alone - so two different
        /// windows onto one frame land in the same bucket.
        /// </summary>
        public bool Equals(RawJson other) =>
            ReferenceEquals(_frame, other._frame) && _offset == other._offset && _length == other._length;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is RawJson other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(_frame?.GetHashCode() ?? 0, _offset, _length);

        /// <summary>Identity comparison; see <see cref="Equals(RawJson)"/>.</summary>
        public static bool operator ==(RawJson left, RawJson right) => left.Equals(right);

        /// <summary>Identity comparison; see <see cref="Equals(RawJson)"/>.</summary>
        public static bool operator !=(RawJson left, RawJson right) => !left.Equals(right);
    }
}
