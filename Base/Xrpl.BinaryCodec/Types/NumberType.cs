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
    /// For non-zero values, mantissa is normalized to [10^18, long.MaxValue (2^63-1)].
    /// Zero is represented as mantissa=0, exponent=Int32.MinValue (-2147483648).
    /// Exponent range: [-32768, 32768].
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

            // Validate zero-representation invariant
            if (mantissa == 0 && exponent != ZeroExponent && exponent != 0)
                throw new FormatException(
                    $"NumberType: zero mantissa requires exponent {ZeroExponent} or 0, got {exponent}");

            // Validate exponent bounds for non-zero values to prevent unbounded BigInteger.Pow
            if (mantissa != 0 && exponent != ZeroExponent &&
                (exponent < MinExponent || exponent > MaxExponent))
                throw new FormatException(
                    $"NumberType: exponent {exponent} out of range [{MinExponent}, {MaxExponent}]");

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
        /// Parse a decimal string (e.g. "10000000000000", "0", "-500", "1e-32000")
        /// into the XRPL Number wire format.
        /// Uses BigInteger-based parsing to handle the full XRPL exponent range (±32768)
        /// which exceeds System.Decimal capacity.
        /// Normalizes mantissa to [10^18, long.MaxValue] per rippled Number class.
        /// </summary>
        public static NumberType FromString(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                throw new FormatException("NumberType: input string must not be empty");

            str = str.Trim();

            // Parse sign
            bool negative = false;
            int pos = 0;
            if (pos < str.Length && str[pos] == '-')
            {
                negative = true;
                pos++;
            }
            else if (pos < str.Length && str[pos] == '+')
            {
                pos++;
            }

            // Parse digits and optional decimal point
            BigInteger integerPart = BigInteger.Zero;
            int fracDigits = 0;
            bool hasDot = false;
            bool hasDigits = false;

            while (pos < str.Length && (char.IsDigit(str[pos]) || str[pos] == '.'))
            {
                if (str[pos] == '.')
                {
                    if (hasDot)
                        throw new FormatException($"NumberType: multiple decimal points in '{str}'");
                    hasDot = true;
                }
                else
                {
                    integerPart = integerPart * 10 + (str[pos] - '0');
                    if (hasDot)
                        fracDigits++;
                    hasDigits = true;
                }
                pos++;
            }

            if (!hasDigits)
                throw new FormatException($"NumberType: no digits in '{str}'");

            // Parse optional scientific notation exponent (e/E)
            int sciExponent = 0;
            if (pos < str.Length && (str[pos] == 'e' || str[pos] == 'E'))
            {
                pos++;
                if (pos >= str.Length)
                    throw new FormatException($"NumberType: incomplete scientific notation in '{str}'");

                bool expNegative = false;
                if (str[pos] == '-')
                {
                    expNegative = true;
                    pos++;
                }
                else if (str[pos] == '+')
                {
                    pos++;
                }

                int expValue = 0;
                bool hasExpDigits = false;
                while (pos < str.Length && char.IsDigit(str[pos]))
                {
                    expValue = expValue * 10 + (str[pos] - '0');
                    if (expValue > 100000) // sanity cap
                        throw new FormatException($"NumberType: exponent too large in '{str}'");
                    hasExpDigits = true;
                    pos++;
                }
                if (!hasExpDigits)
                    throw new FormatException($"NumberType: no digits in exponent of '{str}'");

                sciExponent = expNegative ? -expValue : expValue;
            }

            if (pos != str.Length)
                throw new FormatException($"NumberType: unexpected characters in '{str}' at position {pos}");

            if (integerPart == BigInteger.Zero)
                return new NumberType(0, ZeroExponent);

            // Combined exponent: scientific exponent minus fractional digit count
            int exponent = sciExponent - fracDigits;

            // Normalize mantissa to [10^18, long.MaxValue]
            BigInteger mantissa = integerPart;
            BigInteger bigMinMantissa = new BigInteger(MinMantissa);
            BigInteger bigMaxMantissa = new BigInteger(MaxMantissa);

            while (mantissa < bigMinMantissa)
            {
                mantissa *= 10;
                exponent--;
            }
            while (mantissa > bigMaxMantissa)
            {
                // Round: add 5 before dividing for away-from-zero rounding
                mantissa = (mantissa + 5) / 10;
                exponent++;
            }

            if (exponent < MinExponent || exponent > MaxExponent)
                throw new FormatException(
                    $"NumberType: exponent {exponent} out of range [{MinExponent}, {MaxExponent}]");

            long mantissaLong = (long)mantissa;
            if (negative)
                mantissaLong = -mantissaLong;

            return new NumberType(mantissaLong, exponent);
        }
    }
}
