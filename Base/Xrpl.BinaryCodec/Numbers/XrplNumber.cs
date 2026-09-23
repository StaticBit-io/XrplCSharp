using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;

namespace Xrpl.BinaryCodec.Numbers
{
    /// <summary>
    /// A value of the XRPL <c>Number</c> type, used by the Single Asset Vault (XLS-65) and
    /// Lending Protocol (XLS-66) fields such as <c>AssetsTotal</c>, <c>PrincipalOutstanding</c> or
    /// <c>PeriodicPayment</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is held the way rippled's <c>Number</c> class (<c>include/xrpl/basics/Number.h</c>)
    /// holds it on the large mantissa scale: a sign, a mantissa in [10^18, 10^19 - 1] and an exponent
    /// in [<see cref="MinExponent"/>, <see cref="MaxExponent"/>]. A mantissa above
    /// 9223372036854775807 is always a multiple of ten, so that the value still fits the signed
    /// 64-bit wire form reported by <see cref="Mantissa"/> and <see cref="Exponent"/>.
    /// </para>
    /// <para>
    /// Every value of this type is exact. Parsing and conversions refuse input that the ledger cannot
    /// hold without rounding it, the way rippled refuses such a value in JSON with
    /// "number cannot be represented", instead of signing a value other than the one written.
    /// </para>
    /// <para>
    /// <see cref="ToString()"/> produces the text rippled produces for the field, switching to
    /// scientific notation (<c>1e13</c>) for very large and very small values.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(XrplNumberJsonConverter))]
    public readonly struct XrplNumber : IEquatable<XrplNumber>, IComparable<XrplNumber>, IComparable
    {
        /// <summary>Smallest exponent of a normalized value.</summary>
        public const int MinExponent = -32768;

        /// <summary>Largest exponent of a normalized value.</summary>
        public const int MaxExponent = 32768;

        private const ulong MinInternalMantissa = 1_000_000_000_000_000_000UL;
        private const ulong MaxInternalMantissa = 9_999_999_999_999_999_999UL;
        private const ulong MaxRep = long.MaxValue;
        private const int MantissaLog = 18;
        private const int ZeroExponent = int.MinValue;
        private const int MaxDecimalScale = 28;
        private const long MaxParsedExponent = 100_000;

        private static readonly BigInteger MinInternalMantissaBig = MinInternalMantissa;
        private static readonly BigInteger MaxInternalMantissaBig = MaxInternalMantissa;
        private static readonly BigInteger Ten = 10;
        private static readonly BigInteger MaxDecimal = new BigInteger(decimal.MaxValue);

        private readonly bool _negative;
        private readonly ulong _mantissa;
        private readonly int _exponent;

        /// <summary>The value zero.</summary>
        public static readonly XrplNumber Zero = default;

        private XrplNumber(bool negative, ulong mantissa, int exponent)
        {
            _negative = negative;
            _mantissa = mantissa;
            _exponent = exponent;
        }

        /// <summary>
        /// Creates a value from the wire form: <paramref name="mantissa"/> × 10^<paramref name="exponent"/>.
        /// The pair does not have to be normalized. As in rippled, a value too small for the exponent
        /// range becomes zero.
        /// </summary>
        /// <exception cref="OverflowException">
        /// The value is too large for the exponent range, or <paramref name="mantissa"/> is
        /// <see cref="long.MinValue"/>, which rippled cannot hold without rounding.
        /// </exception>
        public XrplNumber(long mantissa, int exponent)
        {
            bool negative = mantissa < 0;
            BigInteger magnitude = BigInteger.Abs(new BigInteger(mantissa));
            switch (TryNormalize(negative, magnitude, exponent, out XrplNumber value))
            {
                case Outcome.Exact:
                case Outcome.Underflow:
                    this = value;
                    return;
                case Outcome.Inexact:
                    throw new OverflowException($"XrplNumber: mantissa {mantissa} cannot be represented exactly.");
                default:
                    throw new OverflowException($"XrplNumber: {mantissa}e{exponent} is too large to represent.");
            }
        }

        /// <summary>
        /// Mantissa of the wire form: signed, at most 9223372036854775807 in absolute value, 0 for zero.
        /// Matches rippled's <c>Number::mantissa()</c>.
        /// </summary>
        public long Mantissa
        {
            get
            {
                if (_mantissa == 0)
                    return 0;

                ulong external = _mantissa > MaxRep ? _mantissa / 10 : _mantissa;
                return _negative ? -(long)external : (long)external;
            }
        }

        /// <summary>
        /// Exponent of the wire form, <see cref="int.MinValue"/> for zero.
        /// Matches rippled's <c>Number::exponent()</c>.
        /// </summary>
        public int Exponent
        {
            get
            {
                if (_mantissa == 0)
                    return ZeroExponent;

                return _mantissa > MaxRep ? _exponent + 1 : _exponent;
            }
        }

        /// <summary>Whether the value is zero.</summary>
        public bool IsZero => _mantissa == 0;

        /// <summary>Whether the value is below zero.</summary>
        public bool IsNegative => _mantissa != 0 && _negative;

        /// <summary>-1, 0 or 1 by the sign of the value.</summary>
        public int Sign => _mantissa == 0 ? 0 : _negative ? -1 : 1;

        /// <summary>
        /// Parses a decimal or scientific number: <c>"1000"</c>, <c>"-0.025"</c>, <c>"1e13"</c>.
        /// </summary>
        /// <exception cref="FormatException">
        /// The text is not a number, has more significant digits than the ledger holds exactly, or is
        /// out of the exponent range.
        /// </exception>
        public static XrplNumber Parse(string text)
        {
            if (!TryParse(text, out XrplNumber value, out string error))
                throw new FormatException(error);

            return value;
        }

        /// <summary>
        /// Parses a number the way <see cref="Parse(string)"/> does, returning <c>false</c> instead of
        /// throwing.
        /// </summary>
        public static bool TryParse(string text, out XrplNumber value) => TryParse(text, out value, out _);

        /// <summary>
        /// Converts to <see cref="decimal"/>. A value with more fractional digits than
        /// <see cref="decimal"/> holds (28) is rounded to nearest, ties to even; a value too small for
        /// that scale becomes zero.
        /// </summary>
        /// <returns><c>false</c> when the value is beyond the range of <see cref="decimal"/>.</returns>
        public bool TryToDecimal(out decimal value)
        {
            value = 0m;
            if (_mantissa == 0)
                return true;

            ulong mantissa = _mantissa;
            long exponent = _exponent;
            while (mantissa % 10 == 0)
            {
                mantissa /= 10;
                exponent++;
            }

            if (exponent >= 0)
            {
                if (exponent > MaxDecimalScale)
                    return false;

                BigInteger magnitude = new BigInteger(mantissa) * BigInteger.Pow(Ten, (int)exponent);
                if (magnitude > MaxDecimal)
                    return false;

                decimal integral = (decimal)magnitude;
                value = _negative ? -integral : integral;
                return true;
            }

            long scale = -exponent;
            if (scale > MaxDecimalScale)
            {
                mantissa = RoundHalfEven(mantissa, scale - MaxDecimalScale);
                scale = MaxDecimalScale;
            }

            value = new decimal(
                unchecked((int)(uint)mantissa),
                unchecked((int)(uint)(mantissa >> 32)),
                0,
                _negative && mantissa != 0,
                (byte)scale);
            return true;
        }

        /// <summary>
        /// Converts to <see cref="decimal"/> as <see cref="TryToDecimal"/> does.
        /// </summary>
        /// <exception cref="OverflowException">The value is beyond the range of <see cref="decimal"/>.</exception>
        public static explicit operator decimal(XrplNumber number)
        {
            if (!number.TryToDecimal(out decimal value))
                throw new OverflowException($"XrplNumber: {number} is outside the range of System.Decimal.");

            return value;
        }

        /// <summary>
        /// Converts from <see cref="decimal"/>.
        /// </summary>
        /// <exception cref="OverflowException">
        /// The value has more significant digits than the ledger holds exactly; round it first.
        /// </exception>
        public static explicit operator XrplNumber(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            bool negative = (bits[3] & int.MinValue) != 0;
            int scale = (bits[3] >> 16) & 0xFF;
            BigInteger magnitude = (new BigInteger((uint)bits[2]) << 64)
                | (new BigInteger((uint)bits[1]) << 32)
                | new BigInteger((uint)bits[0]);

            if (TryNormalize(negative, magnitude, -scale, out XrplNumber number) != Outcome.Exact)
                throw new OverflowException(
                    $"XrplNumber: {value.ToString(CultureInfo.InvariantCulture)} cannot be represented exactly.");

            return number;
        }

        /// <summary>Converts from <see cref="int"/>; always exact.</summary>
        public static implicit operator XrplNumber(int value) => new XrplNumber(value, 0);

        /// <summary>Converts from <see cref="long"/>.</summary>
        /// <exception cref="OverflowException">The value is <see cref="long.MinValue"/>.</exception>
        public static explicit operator XrplNumber(long value) => new XrplNumber(value, 0);

        /// <summary>
        /// The value as rippled writes it in JSON (<c>to_string(Number)</c>).
        /// </summary>
        public override string ToString()
        {
            if (_mantissa == 0)
                return "0";

            ulong mantissa = _mantissa;
            int exponent = _exponent;
            string sign = _negative ? "-" : string.Empty;

            if (exponent != 0 && (exponent < -(MantissaLog + 10) || exponent > -(MantissaLog - 10)))
            {
                while (mantissa % 10 == 0 && exponent < MaxExponent)
                {
                    mantissa /= 10;
                    exponent++;
                }

                string digits = mantissa.ToString(CultureInfo.InvariantCulture);
                return exponent == 0
                    ? sign + digits
                    : sign + digits + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            }

            const int padPrefix = MantissaLog + 12;
            const int padSuffix = MantissaLog + 8;

            string padded = new string('0', padPrefix)
                + mantissa.ToString(CultureInfo.InvariantCulture)
                + new string('0', padSuffix);
            int offset = exponent + padPrefix + MantissaLog + 1;

            int integerFrom = offset > padPrefix ? padPrefix : 0;
            while (integerFrom < offset && padded[integerFrom] == '0')
                integerFrom++;

            int fractionTo = padded.Length - offset > padSuffix ? padded.Length - padSuffix : padded.Length;
            while (fractionTo > offset && padded[fractionTo - 1] == '0')
                fractionTo--;

            StringBuilder builder = new StringBuilder(sign, padded.Length);
            if (integerFrom == offset)
                builder.Append('0');
            else
                builder.Append(padded, integerFrom, offset - integerFrom);

            if (fractionTo != offset)
                builder.Append('.').Append(padded, offset, fractionTo - offset);

            return builder.ToString();
        }

        /// <inheritdoc />
        public bool Equals(XrplNumber other)
        {
            if (_mantissa == 0 || other._mantissa == 0)
                return _mantissa == other._mantissa;

            return _negative == other._negative && _mantissa == other._mantissa && _exponent == other._exponent;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is XrplNumber other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            if (_mantissa == 0)
                return 0;

            unchecked
            {
                int hash = _mantissa.GetHashCode();
                hash = (hash * 397) ^ _exponent;
                return _negative ? ~hash : hash;
            }
        }

        /// <inheritdoc />
        public int CompareTo(XrplNumber other)
        {
            int sign = Sign;
            int otherSign = other.Sign;
            if (sign != otherSign)
                return sign.CompareTo(otherSign);

            if (sign == 0)
                return 0;

            int magnitude = _exponent != other._exponent
                ? _exponent.CompareTo(other._exponent)
                : _mantissa.CompareTo(other._mantissa);

            return sign < 0 ? -magnitude : magnitude;
        }

        /// <inheritdoc />
        public int CompareTo(object obj)
        {
            if (obj is null)
                return 1;

            if (obj is XrplNumber other)
                return CompareTo(other);

            throw new ArgumentException($"Object must be of type {nameof(XrplNumber)}.", nameof(obj));
        }

        /// <summary>Equality by value.</summary>
        public static bool operator ==(XrplNumber left, XrplNumber right) => left.Equals(right);

        /// <summary>Inequality by value.</summary>
        public static bool operator !=(XrplNumber left, XrplNumber right) => !left.Equals(right);

        /// <summary>Less than.</summary>
        public static bool operator <(XrplNumber left, XrplNumber right) => left.CompareTo(right) < 0;

        /// <summary>Greater than.</summary>
        public static bool operator >(XrplNumber left, XrplNumber right) => left.CompareTo(right) > 0;

        /// <summary>Less than or equal.</summary>
        public static bool operator <=(XrplNumber left, XrplNumber right) => left.CompareTo(right) <= 0;

        /// <summary>Greater than or equal.</summary>
        public static bool operator >=(XrplNumber left, XrplNumber right) => left.CompareTo(right) >= 0;

        private enum Outcome
        {
            Exact,
            Inexact,
            Underflow,
            Overflow,
        }

        /// <summary>
        /// Brings magnitude × 10^exponent into the canonical form without rounding. Mirrors the range
        /// handling of rippled's <c>doNormalize</c>, reporting where it would have had to round.
        /// </summary>
        private static Outcome TryNormalize(bool negative, BigInteger magnitude, long exponent, out XrplNumber value)
        {
            value = Zero;
            if (magnitude.IsZero)
                return Outcome.Exact;

            while (magnitude > MaxInternalMantissaBig)
            {
                BigInteger quotient = BigInteger.DivRem(magnitude, Ten, out BigInteger remainder);
                if (!remainder.IsZero)
                    return Outcome.Inexact;

                magnitude = quotient;
                exponent++;
            }

            while (magnitude < MinInternalMantissaBig)
            {
                magnitude *= Ten;
                exponent--;
            }

            ulong mantissa = (ulong)magnitude;
            if (mantissa > MaxRep && mantissa % 10 != 0)
                return Outcome.Inexact;

            if (exponent > MaxExponent)
                return Outcome.Overflow;

            if (exponent < MinExponent)
                return Outcome.Underflow;

            value = new XrplNumber(negative, mantissa, (int)exponent);
            return Outcome.Exact;
        }

        private static ulong RoundHalfEven(ulong mantissa, long droppedDigits)
        {
            // 10^19 is the largest power of ten a ulong holds; any more digits leave less than half.
            if (droppedDigits > 19)
                return 0;

            ulong divisor = 1;
            for (long i = 0; i < droppedDigits; i++)
                divisor *= 10;

            ulong quotient = mantissa / divisor;
            ulong remainder = mantissa % divisor;
            ulong half = divisor / 2;
            if (remainder > half || (remainder == half && (quotient & 1) == 1))
                quotient++;

            return quotient;
        }

        private static bool TryParse(string text, out XrplNumber value, out string error)
        {
            value = Zero;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "XrplNumber: input string must not be empty.";
                return false;
            }

            string trimmed = text.Trim();
            int position = 0;
            bool negative = false;
            if (trimmed[position] == '-' || trimmed[position] == '+')
            {
                negative = trimmed[position] == '-';
                position++;
            }

            BigInteger digits = BigInteger.Zero;
            long fractionDigits = 0;
            bool hasDot = false;
            bool hasDigits = false;
            for (; position < trimmed.Length; position++)
            {
                char c = trimmed[position];
                if (c == '.')
                {
                    if (hasDot)
                    {
                        error = $"XrplNumber: multiple decimal points in '{trimmed}'.";
                        return false;
                    }

                    hasDot = true;
                }
                else if (c >= '0' && c <= '9')
                {
                    digits = digits * Ten + (c - '0');
                    if (hasDot)
                        fractionDigits++;
                    hasDigits = true;
                }
                else
                {
                    break;
                }
            }

            if (!hasDigits)
            {
                error = $"XrplNumber: no digits in '{trimmed}'.";
                return false;
            }

            long scientificExponent = 0;
            if (position < trimmed.Length && (trimmed[position] == 'e' || trimmed[position] == 'E'))
            {
                position++;
                bool exponentNegative = false;
                if (position < trimmed.Length && (trimmed[position] == '-' || trimmed[position] == '+'))
                {
                    exponentNegative = trimmed[position] == '-';
                    position++;
                }

                bool hasExponentDigits = false;
                for (; position < trimmed.Length && trimmed[position] >= '0' && trimmed[position] <= '9'; position++)
                {
                    scientificExponent = scientificExponent * 10 + (trimmed[position] - '0');
                    if (scientificExponent > MaxParsedExponent)
                    {
                        error = $"XrplNumber: exponent too large in '{trimmed}'.";
                        return false;
                    }

                    hasExponentDigits = true;
                }

                if (!hasExponentDigits)
                {
                    error = $"XrplNumber: no digits in the exponent of '{trimmed}'.";
                    return false;
                }

                if (exponentNegative)
                    scientificExponent = -scientificExponent;
            }

            if (position != trimmed.Length)
            {
                error = $"XrplNumber: unexpected character in '{trimmed}' at position {position}.";
                return false;
            }

            switch (TryNormalize(negative, digits, scientificExponent - fractionDigits, out value))
            {
                case Outcome.Exact:
                    error = null;
                    return true;
                case Outcome.Inexact:
                    error = $"XrplNumber: '{trimmed}' cannot be represented exactly; the ledger holds at most "
                        + "19 significant digits, and 18 above 9223372036854775807.";
                    break;
                case Outcome.Underflow:
                    error = $"XrplNumber: '{trimmed}' is too small to represent exactly.";
                    break;
                default:
                    error = $"XrplNumber: '{trimmed}' is too large to represent.";
                    break;
            }

            value = Zero;
            return false;
        }
    }
}
