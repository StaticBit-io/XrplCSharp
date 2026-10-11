using System;
using System.Text;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Common
{
    /// <summary>
    /// The bytes of an Oracle <c>Provider</c>, <c>AssetClass</c> or <c>URI</c> field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are Blob fields: rippled checks their length and nothing else
    /// (<c>OracleSet::preflight</c>), so an oracle published by anyone may hold arbitrary bytes.
    /// The value is therefore the bytes, not a string. A string could not tell the text
    /// <c>C328FF</c> from the bytes C3 28 FF, and writing it back would sign the hex digits as text.
    /// What is read is written back byte for byte.
    /// </para>
    /// <para>
    /// A string converts implicitly, as text, so <c>Provider = "chainlink"</c> stores the ASCII
    /// bytes of <c>chainlink</c>. Bytes given as hex go through <see cref="FromHex"/>. Reading the
    /// value as text is <see cref="TryGetText"/>, which declines bytes that are not printable ASCII.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(OracleBlobConverter))]
    public sealed class OracleBlob : IEquatable<OracleBlob>
    {
        private readonly byte[] _bytes;

        private OracleBlob(byte[] bytes)
        {
            _bytes = bytes;
        }

        /// <summary>
        /// The bytes of the field.
        /// </summary>
        public ReadOnlySpan<byte> Bytes => _bytes;

        /// <summary>
        /// The bytes as UPPERCASE hex, which is how rippled writes a Blob field in JSON.
        /// </summary>
        public string Hex => Convert.ToHexString(_bytes);

        /// <summary>
        /// Whether the bytes are printable ASCII (0x20-0x7E), the form these fields take when they
        /// hold text.
        /// </summary>
        public bool IsText => OracleAsciiValidation.IsPrintableAscii(_bytes);

        /// <summary>
        /// Creates the value from text, as its ASCII bytes.
        /// </summary>
        /// <param name="text">Printable ASCII text (U+0020-U+007E).</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="text"/> holds a character outside printable ASCII, which has no single
        /// byte to stand for it.
        /// </exception>
        public static OracleBlob FromText(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < (char)0x20 || c > (char)0x7E)
                {
                    throw new ArgumentException(
                        $"Oracle text must use printable ASCII (U+0020-U+007E). Invalid character U+{(ushort)c:X4} at index {i}; pass bytes through FromHex or FromBytes instead.",
                        nameof(text));
                }
            }

            return new OracleBlob(Encoding.ASCII.GetBytes(text));
        }

        /// <summary>
        /// Creates the value from its bytes written as hex, in either case.
        /// </summary>
        /// <param name="hex">An even-length hex string; an empty string is zero bytes.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hex"/> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="hex"/> is not an even-length hex string.</exception>
        public static OracleBlob FromHex(string hex)
        {
            if (hex is null)
                throw new ArgumentNullException(nameof(hex));

            if (hex.Length % 2 != 0 || !OracleAsciiValidation.IsHexString(hex.AsSpan()))
                throw new FormatException("An Oracle Blob value must be an even-length hex string.");

            return new OracleBlob(Convert.FromHexString(hex));
        }

        /// <summary>
        /// Creates the value from its bytes, which are copied.
        /// </summary>
        public static OracleBlob FromBytes(ReadOnlySpan<byte> bytes) => new OracleBlob(bytes.ToArray());

        /// <summary>
        /// Converts text to the value, as <see cref="FromText"/> does; <c>null</c> stays <c>null</c>.
        /// </summary>
        /// <exception cref="ArgumentException">The text is not printable ASCII.</exception>
        public static implicit operator OracleBlob(string text) => text is null ? null : FromText(text);

        /// <summary>
        /// Reads the bytes as text when they are printable ASCII.
        /// </summary>
        /// <param name="text">The text, or <c>null</c> when the bytes are not printable ASCII.</param>
        /// <returns>Whether the bytes are text.</returns>
        public bool TryGetText(out string text)
        {
            if (IsText)
            {
                text = Encoding.ASCII.GetString(_bytes);
                return true;
            }

            text = null;
            return false;
        }

        /// <summary>
        /// Returns a copy of the bytes.
        /// </summary>
        public byte[] ToArray() => (byte[])_bytes.Clone();

        /// <summary>
        /// Returns <see cref="Hex"/>, the one form that stands for every value unambiguously.
        /// </summary>
        public override string ToString() => Hex;

        /// <inheritdoc />
        public bool Equals(OracleBlob other) =>
            other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is OracleBlob other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.AddBytes(_bytes);
            return hash.ToHashCode();
        }

        /// <summary>Compares two values by their bytes.</summary>
        public static bool operator ==(OracleBlob left, OracleBlob right) =>
            left is null ? right is null : left.Equals(right);

        /// <summary>Compares two values by their bytes.</summary>
        public static bool operator !=(OracleBlob left, OracleBlob right) => !(left == right);
    }
}
