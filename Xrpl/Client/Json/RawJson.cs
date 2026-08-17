using System;
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
    /// </remarks>
    public readonly struct RawJson
    {
        private readonly byte[]? _frame;
        private readonly int _offset;
        private readonly int _length;

        /// <summary>Records the window; does not copy the frame.</summary>
        public RawJson(byte[] frame, int offset, int length)
        {
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

        /// <summary>Copies the captured JSON into a new array.</summary>
        public byte[] ToArray() => _frame is null ? Array.Empty<byte>() : Span.ToArray();

        /// <summary>Writes the captured JSON into <paramref name="writer"/> verbatim.</summary>
        public void WriteTo(Utf8JsonWriter writer)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (_frame is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteRawValue(Span, skipInputValidation: true);
        }

        /// <summary>Materializes the captured JSON as text. Allocates; call only when text is needed.</summary>
        public override string ToString()
        {
            return _frame is null ? string.Empty : Encoding.UTF8.GetString(_frame, _offset, _length);
        }
    }
}
