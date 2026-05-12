using System;
using System.Globalization;
using System.Numerics;
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

            // Use BigInteger to avoid decimal overflow for large exponents.
            // The protocol allows exponents up to ±32768 which far exceeds System.Decimal range.
            BigInteger abs = BigInteger.Abs(new BigInteger(Mantissa));
            bool negative = Mantissa < 0;
            int exp = Exponent;

            if (exp >= 0)
            {
                // mantissa × 10^exp — always integer
                BigInteger result = abs * BigInteger.Pow(10, exp);
                string str = result.ToString(CultureInfo.InvariantCulture);
                return JsonValue.Create(negative ? "-" + str : str);
            }
            else
            {
                // exp < 0: mantissa / 10^|exp| — may have fractional part
                int absExp = -exp;
                string digits = abs.ToString(CultureInfo.InvariantCulture);

                string result;
                if (digits.Length > absExp)
                {
                    // Insert decimal point: e.g. "12345" with exp=-2 → "123.45"
                    int pointPos = digits.Length - absExp;
                    string intPart = digits.Substring(0, pointPos);
                    string fracPart = digits.Substring(pointPos).TrimEnd('0');
                    result = fracPart.Length > 0 ? intPart + "." + fracPart : intPart;
                }
                else
                {
                    // Leading zeros: e.g. "5" with exp=-3 → "0.005"
                    string fracPart = (new string('0', absExp - digits.Length) + digits).TrimEnd('0');
                    result = fracPart.Length > 0 ? "0." + fracPart : "0";
                }

                return JsonValue.Create(negative ? "-" + result : result);
            }
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
