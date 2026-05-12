using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Util;

namespace Xrpl.BinaryCodec.Types
{
    /// <summary>
    /// XRPL Number type (serialized type code 9). Serialized as 12 bytes big-endian:
    ///   8 bytes — mantissa (signed int64, big-endian)
    ///   4 bytes — exponent (signed int32, big-endian)
    /// Mantissa is normalized to [10^15, 10^16-1] for non-zero values.
    /// Zero is represented as mantissa=0, exponent=Int32.MinValue (-2147483648).
    /// Used by Loan/LoanBroker fields (PrincipalRequested, DebtMaximum, etc.) per XLS-66.
    /// Encoding matches rippled Number class (include/xrpl/basics/Number.h).
    /// </summary>
    public class NumberType : ISerializedType
    {
        private const long MinMantissa = 1_000_000_000_000_000_000L;   // 10^18
        private const long MaxMantissa = 9_223_372_036_854_775_807L; // long.MaxValue (2^63 - 1)
        private const int MinExponent = -32768;
        private const int MaxExponent = 32768;
        private const int ZeroExponent = int.MinValue; // -2147483648

        public readonly long Mantissa;
        public readonly int Exponent;

        public NumberType(long mantissa, int exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        public void ToBytes(IBytesSink sink)
        {
            sink.Put(Bits.GetBytes(Mantissa));
            sink.Put(Bits.GetBytes(Exponent));
        }

        public static NumberType FromParser(BinaryParser parser, int? hint = null)
        {
            byte[] mantissaBytes = parser.Read(8);
            byte[] exponentBytes = parser.Read(4);
            long mantissa = Bits.ToInt64(mantissaBytes, 0);
            int exponent = Bits.ToInt32(exponentBytes, 0);
            return new NumberType(mantissa, exponent);
        }

        public JsonNode ToJson()
        {
            if (Mantissa == 0)
                return JsonValue.Create("0");

            // Reconstruct decimal value from mantissa × 10^exponent
            decimal value = Mantissa;
            int exp = Exponent;

            if (exp > 0)
            {
                for (int i = 0; i < exp; i++)
                    value *= 10m;
            }
            else if (exp < 0)
            {
                for (int i = 0; i < -exp; i++)
                    value /= 10m;
            }

            // Return as string to preserve precision
            return JsonValue.Create(value.ToString(CultureInfo.InvariantCulture));
        }

        public override string ToString() => ToJson()?.ToString() ?? "0";

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
        /// Parse a decimal string (e.g. "10000000000000", "0", "-500") into the XRPL Number wire format.
        /// Normalizes mantissa to [10^15, 10^16-1] range per rippled Number class.
        /// </summary>
        public static NumberType FromString(string str)
        {
            decimal value = decimal.Parse(str, NumberStyles.Any, CultureInfo.InvariantCulture);

            if (value == 0m)
                return new NumberType(0, ZeroExponent);

            bool negative = value < 0m;
            if (negative)
                value = -value;

            // Normalize mantissa to [10^15, 10^16-1]
            int exponent = 0;
            while (value < MinMantissa)
            {
                value *= 10m;
                exponent--;
            }
            while (value > MaxMantissa)
            {
                value /= 10m;
                exponent++;
            }

            long mantissa = (long)Math.Round(value, MidpointRounding.AwayFromZero);

            // Re-check after rounding
            if (mantissa > MaxMantissa)
            {
                mantissa /= 10;
                exponent++;
            }

            if (exponent < MinExponent || exponent > MaxExponent)
                throw new FormatException(
                    $"Number exponent {exponent} out of range [{MinExponent}, {MaxExponent}]");

            if (negative)
                mantissa = -mantissa;

            return new NumberType(mantissa, exponent);
        }
    }
}
