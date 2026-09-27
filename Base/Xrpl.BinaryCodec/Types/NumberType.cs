using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// XRPL Number type (serialized type code 9). Serialized as 12 bytes big-endian:
    ///   8 bytes — mantissa (signed int64, big-endian)
    ///   4 bytes — exponent (signed int32, big-endian)
    /// The value itself, its parsing and its text form are <see cref="XrplNumber"/>; this type keeps
    /// the wire pair as it was read or produced.
    /// Used by Vault (XLS-65) and Loan/LoanBroker (XLS-66) fields such as PrincipalRequested and DebtMaximum.
    /// Encoding matches rippled Number class (include/xrpl/basics/Number.h).
    /// </summary>
    public class NumberType : ISerializedType
    {
        private const int ZeroExponent = int.MinValue; // -2147483648

        /// <summary>Mantissa of the wire pair: signed, 0 for zero.</summary>
        public readonly long Mantissa;

        /// <summary>Exponent of the wire pair: <see cref="int.MinValue"/> (or 0) for zero.</summary>
        public readonly int Exponent;

        /// <summary>
        /// Creates the wire pair as given.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The mantissa is zero and the exponent is neither <see cref="int.MinValue"/> nor 0, the two zero encodings.
        /// </exception>
        /// <exception cref="OverflowException">The pair is not a value rippled can hold; see <see cref="XrplNumber(long, int)"/>.</exception>
        public NumberType(long mantissa, int exponent)
        {
            if (mantissa == 0 && exponent != ZeroExponent && exponent != 0)
                throw new ArgumentOutOfRangeException(
                    nameof(exponent),
                    $"NumberType: zero mantissa requires exponent {ZeroExponent} or 0, got {exponent}");

            Value = new XrplNumber(mantissa, exponent);
            Mantissa = mantissa;
            Exponent = exponent;
        }

        /// <summary>
        /// Creates the canonical wire pair of <paramref name="value"/>.
        /// </summary>
        public NumberType(XrplNumber value)
        {
            Value = value;
            Mantissa = value.Mantissa;
            Exponent = value.Exponent;
        }

        /// <summary>The value the wire pair holds.</summary>
        public XrplNumber Value { get; }

        /// <summary>Writes the wire pair: 8-byte mantissa, then 4-byte exponent, both big-endian.</summary>
        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Bits.GetBytes(Mantissa));
            sink.Put(Bits.GetBytes(Exponent));
        }

        /// <summary>Reads the 12-byte wire pair and keeps it as read.</summary>
        /// <exception cref="FormatException">The bytes are not a value rippled can hold.</exception>
        public static NumberType FromParser(BinaryParser parser, int? hint = null)
        {
            byte[] mantissaBytes = parser.Read(8);
            byte[] exponentBytes = parser.Read(4);
            long mantissa = Bits.ToInt64(mantissaBytes, 0);
            int exponent = Bits.ToInt32(exponentBytes, 0);

            try
            {
                return new NumberType(mantissa, exponent);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new FormatException(exception.Message, exception);
            }
            catch (OverflowException exception)
            {
                throw new FormatException($"NumberType: {exception.Message}", exception);
            }
        }

        /// <summary>
        /// The value as rippled writes it: decimal, or scientific notation (<c>1e13</c>) for very
        /// large and very small values.
        /// </summary>
        public JsonNode ToJson() => JsonValue.Create(Value.ToString());

        /// <inheritdoc cref="XrplNumber.ToString()"/>
        public override string ToString() => Value.ToString();

        /// <summary>Reads a JSON string or number into the canonical wire pair.</summary>
        /// <exception cref="FormatException">
        /// The token is neither a string nor a number, is not a number, or the ledger cannot hold it exactly.
        /// </exception>
        public static NumberType FromJson(JsonNode token)
        {
            JsonValueKind kind = token.GetValueKind();
            string str;
            if (kind == JsonValueKind.Number)
            {
                // JsonNode numeric values may be stored as int, long, or double internally.
                // Use ToString() which always returns the numeric value as a string.
                str = token.ToString();
            }
            else if (kind == JsonValueKind.String)
            {
                str = token.GetValue<string>();
            }
            else
            {
                throw new FormatException($"Cannot parse Number from JSON kind {kind}");
            }

            return FromString(str);
        }

        /// <summary>
        /// Parses a decimal or scientific string (e.g. "10000000000000", "0", "-500", "1e-32000") into
        /// the canonical wire pair. See <see cref="XrplNumber.Parse(string)"/>.
        /// </summary>
        /// <exception cref="FormatException">
        /// The string is not a number, or the ledger cannot hold it exactly.
        /// </exception>
        public static NumberType FromString(string str) => new NumberType(XrplNumber.Parse(str));
    }
}
