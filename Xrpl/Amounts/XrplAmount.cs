using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// An amount of one asset, held the way rippled's <c>STAmount</c> holds it: XRP as a whole
    /// number of drops, an MPT as a whole number of units, an issued currency as a 16-digit
    /// mantissa with an exponent from -96 to 80. The whole range of the ledger is representable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Currency"/> is the wire model and keeps its value as text; this is the value to
    /// compute with. <see cref="XrplAmountMath"/> holds the ledger's arithmetic on it and
    /// <see cref="XrplQuality"/> the exchange rate of an offer.
    /// </para>
    /// <para>
    /// A value is always canonical: every way of building one rounds it to what the asset can
    /// hold, as <c>STAmount::canonicalize</c> does.
    /// </para>
    /// </remarks>
    public readonly struct XrplAmount : IEquatable<XrplAmount>, IComparable<XrplAmount>
    {
        /// <summary>Smallest mantissa of a non-zero issued-currency amount (<c>STAmount::kMinValue</c>).</summary>
        public const ulong MinIouMantissa = 1_000_000_000_000_000;

        /// <summary>Largest mantissa of an issued-currency amount (<c>STAmount::kMaxValue</c>).</summary>
        public const ulong MaxIouMantissa = 9_999_999_999_999_999;

        /// <summary>Smallest exponent of a non-zero issued-currency amount (<c>STAmount::kMinOffset</c>).</summary>
        public const int MinIouExponent = -96;

        /// <summary>Largest exponent of an issued-currency amount (<c>STAmount::kMaxOffset</c>).</summary>
        public const int MaxIouExponent = 80;

        /// <summary>Largest amount of XRP, in drops (<c>STAmount::kMaxNativeN</c>).</summary>
        public const ulong MaxDrops = 100_000_000_000_000_000;

        /// <summary>Largest amount of an MPT (<c>kMaxMpTokenAmount</c>).</summary>
        public const ulong MaxMptAmount = 0x7FFF_FFFF_FFFF_FFFF;

        /// <summary>The exponent rippled gives an issued-currency zero, so that it sorts below every positive value.</summary>
        internal const int IouZeroExponent = -100;

        private const int IouDigits = 16;

        private static readonly Regex AmountText = new Regex(
            @"^([-+])?(?=\.?\d)(\d+)?(\.(\d*))?([eE]([+-]?\d+))?$",
            RegexOptions.CultureInvariant);

        private readonly IssuedCurrency _asset;

        private XrplAmount(IssuedCurrency asset, AmountKind kind, ulong mantissa, int exponent, bool negative)
        {
            _asset = asset;
            Kind = kind;
            StMantissa = mantissa;
            StExponent = exponent;
            IsNegative = negative && mantissa != 0;
        }

        /// <summary>The asset: XRP, a currency with its issuer, or an MPT issuance.</summary>
        /// <remarks>null only for the unitless rates <see cref="XrplQuality"/> works with.</remarks>
        public IssuedCurrency Asset => _asset;

        /// <summary>What the asset is, which decides how the amount rounds.</summary>
        public AmountKind Kind { get; }

        /// <summary>Whether the amount is XRP or an MPT, which hold whole units only.</summary>
        public bool IsIntegral => Kind != AmountKind.Iou;

        /// <summary>Whether the amount is zero.</summary>
        public bool IsZero => StMantissa == 0;

        /// <summary>Whether the amount is below zero.</summary>
        public bool IsNegative { get; }

        /// <summary>
        /// The value: drops for XRP, units for an MPT, the currency's own unit for an issued
        /// currency.
        /// </summary>
        public XrplNumber Value => StMantissa == 0
            ? XrplNumber.Zero
            : new XrplNumber(IsNegative ? -(long)StMantissa : (long)StMantissa, StExponent);

        /// <summary>
        /// <c>STAmount::mantissa()</c>: drops or units, or for an issued currency a mantissa in
        /// [10^15, 10^16); zero for zero.
        /// </summary>
        internal ulong StMantissa { get; }

        /// <summary>
        /// <c>STAmount::exponent()</c>: 0 for XRP and MPT, -100 for an issued-currency zero.
        /// </summary>
        internal int StExponent { get; }

        /// <summary>
        /// Which <see cref="AmountKind"/> an asset is. No asset at all is the unitless rate rippled
        /// calls <c>noIssue()</c>, which rounds like an issued currency.
        /// </summary>
        public static AmountKind KindOf(IssuedCurrency asset)
        {
            if (asset == null)
                return AmountKind.Iou;

            return asset.IsMpt() ? AmountKind.Mpt : asset.IsXrp() ? AmountKind.Xrp : AmountKind.Iou;
        }

        /// <summary>A zero amount of the asset.</summary>
        public static XrplAmount Zero(IssuedCurrency asset)
        {
            AmountKind kind = KindOf(asset);
            return new XrplAmount(asset, kind, 0, kind == AmountKind.Iou ? IouZeroExponent : 0, false);
        }

        /// <summary>
        /// <c>STAmount{asset, value}</c> (<c>STAmount::fromNumber</c>): the magnitude rounded to what
        /// the asset holds - a whole drop or unit for XRP and MPT, 16 significant digits for an
        /// issued currency - in <paramref name="rounding"/>, and the sign put back. A directed mode
        /// therefore applies to the magnitude: <see cref="NumberRounding.Upward"/> rounds away
        /// from zero.
        /// </summary>
        /// <exception cref="OverflowException">The value is beyond what the asset can hold.</exception>
        public static XrplAmount FromNumber(IssuedCurrency asset, XrplNumber value, NumberRounding rounding = NumberRounding.ToNearest)
        {
            AmountKind kind = KindOf(asset);
            if (value.IsZero)
                return Zero(asset);

            BigInteger mantissa = BigInteger.Abs(value.Mantissa);
            int exponent = value.Exponent;
            if (kind == AmountKind.Iou)
            {
                XrplAmount magnitude = Canonical(asset, kind, false, mantissa, exponent, rounding);
                return value.IsNegative ? -magnitude : magnitude;
            }

            // static_cast<std::int64_t>(Number): rounded straight to an integer, with no cutoff for
            // a tiny value - rounding up makes 3e-18 one drop.
            BigInteger units;
            if (exponent >= 0)
            {
                units = mantissa * BigInteger.Pow(10, exponent);
                if (units > long.MaxValue)
                    throw new OverflowException("Number::operator rep() overflow");
            }
            else
            {
                units = RoundedQuotient(mantissa, BigInteger.Pow(10, -exponent), false, rounding);
            }

            return FromStParts(asset, kind, (ulong)units, 0, value.IsNegative, rounding);
        }

        /// <summary>
        /// Reads an amount the way rippled's <c>amountFromString</c> does: XRP in drops, an MPT in
        /// units, an issued currency in decimal or scientific notation, rounded to nearest.
        /// </summary>
        /// <exception cref="FormatException">The text is not a number, or XRP or an MPT with a fraction.</exception>
        /// <exception cref="OverflowException">The value is beyond what the asset can hold.</exception>
        public static XrplAmount Parse(IssuedCurrency asset, string value)
        {
            if (!TryParseParts(value, out bool negative, out BigInteger mantissa, out int exponent))
                throw new FormatException($"'{value}' is not an amount.");

            AmountKind kind = KindOf(asset);
            if (kind != AmountKind.Iou && exponent < 0 && !mantissa.IsZero)
                throw new FormatException("XRP and MPT must be specified as integral amount.");

            return mantissa.IsZero ? Zero(asset) : Canonical(asset, kind, negative, mantissa, exponent, NumberRounding.ToNearest);
        }

        /// <summary>
        /// <see cref="Parse"/> that reports failure instead of throwing, for a malformed value or
        /// one beyond what the asset can hold.
        /// </summary>
        public static bool TryParse(IssuedCurrency asset, string value, out XrplAmount amount)
        {
            try
            {
                amount = Parse(asset, value);
                return true;
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                amount = default;
                return false;
            }
        }

        /// <summary>The amount a <see cref="Currency"/> carries, read over the ledger's whole range.</summary>
        /// <exception cref="FormatException">The value is not an amount.</exception>
        /// <exception cref="OverflowException">The value is beyond what the asset can hold.</exception>
        public static XrplAmount FromCurrency(Currency currency)
        {
            if (currency == null)
                throw new ArgumentNullException(nameof(currency));

            return Parse(AssetOf(currency), string.IsNullOrWhiteSpace(currency.Value) ? "0" : currency.Value);
        }

        /// <summary>The asset a <see cref="Currency"/> is an amount of.</summary>
        public static IssuedCurrency AssetOf(Currency currency)
        {
            if (currency == null)
                throw new ArgumentNullException(nameof(currency));

            if (!string.IsNullOrWhiteSpace(currency.MPTokenIssuanceID))
                return new IssuedCurrency { MptIssuanceId = currency.MPTokenIssuanceID };

            return currency.IsXrp()
                ? new IssuedCurrency { Currency = "XRP" }
                : new IssuedCurrency { Currency = currency.CurrencyCode, Issuer = currency.Issuer };
        }

        /// <summary>The amount as the wire model, its value written the way rippled writes it.</summary>
        public Currency ToCurrency()
        {
            if (_asset == null)
                throw new InvalidOperationException("A rate has no asset to write it as.");

            return Kind switch
            {
                AmountKind.Xrp => new Currency { CurrencyCode = "XRP", Value = ToString() },
                AmountKind.Mpt => new Currency { MPTokenIssuanceID = _asset.MptIssuanceId, Value = ToString() },
                _ => new Currency { CurrencyCode = _asset.Currency, Issuer = _asset.Issuer, Value = ToString() },
            };
        }

        /// <summary>Whether two assets are the same asset.</summary>
        public static bool SameAsset(IssuedCurrency a, IssuedCurrency b)
        {
            if (a == null || b == null)
                return a == null && b == null;

            AmountKind kind = KindOf(a);
            if (kind != KindOf(b))
                return false;

            return kind switch
            {
                AmountKind.Xrp => true,
                AmountKind.Mpt => string.Equals(a.MptIssuanceId, b.MptIssuanceId, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(a.Currency, b.Currency, StringComparison.Ordinal) &&
                     string.Equals(a.Issuer, b.Issuer, StringComparison.Ordinal),
            };
        }

        /// <summary>The same amount with the other sign.</summary>
        public static XrplAmount operator -(XrplAmount amount) =>
            new XrplAmount(amount._asset, amount.Kind, amount.StMantissa, amount.StExponent, !amount.IsNegative);

        /// <summary>
        /// <c>STAmount::getText</c>: drops and MPT units as an integer; an issued currency in plain
        /// decimal, or in scientific notation when its exponent is below -25 or above -5.
        /// </summary>
        public override string ToString()
        {
            if (StMantissa == 0)
                return "0";

            string raw = StMantissa.ToString(CultureInfo.InvariantCulture);
            StringBuilder text = new StringBuilder();
            if (IsNegative)
                text.Append('-');

            bool scientific = StExponent != 0 && (StExponent < -25 || StExponent > -5);
            if (Kind != AmountKind.Iou || scientific)
            {
                text.Append(raw);
                if (scientific)
                    text.Append('e').Append(StExponent.ToString(CultureInfo.InvariantCulture));

                return text.ToString();
            }

            const int padPrefix = 27;
            const int padSuffix = 23;
            string padded = new string('0', padPrefix) + raw + new string('0', padSuffix);
            int point = StExponent + 43;

            string before = padded.Substring(0, point).TrimStart('0');
            string after = padded.Substring(point, padded.Length - point - padSuffix).TrimEnd('0');

            text.Append(before.Length == 0 ? "0" : before);
            if (after.Length > 0)
                text.Append('.').Append(after);

            return text.ToString();
        }

        /// <inheritdoc />
        public bool Equals(XrplAmount other) =>
            SameAsset(_asset, other._asset) && Kind == other.Kind && IsNegative == other.IsNegative &&
            StMantissa == other.StMantissa && StExponent == other.StExponent;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is XrplAmount other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Kind, IsNegative, StMantissa, StExponent);

        /// <summary>Compares two amounts of the same asset by value.</summary>
        /// <exception cref="ArgumentException">The amounts are of different assets.</exception>
        public int CompareTo(XrplAmount other)
        {
            RequireSameAsset(this, other);
            return Value.CompareTo(other.Value);
        }

        public static bool operator ==(XrplAmount left, XrplAmount right) => left.Equals(right);

        public static bool operator !=(XrplAmount left, XrplAmount right) => !left.Equals(right);

        public static bool operator <(XrplAmount left, XrplAmount right) => left.CompareTo(right) < 0;

        public static bool operator >(XrplAmount left, XrplAmount right) => left.CompareTo(right) > 0;

        public static bool operator <=(XrplAmount left, XrplAmount right) => left.CompareTo(right) <= 0;

        public static bool operator >=(XrplAmount left, XrplAmount right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// An XRP or MPT amount of <paramref name="units"/>, anywhere in the <c>int64</c> range, as
        /// the payment engine's <c>XRPAmount</c> and <c>MPTAmount</c> hold it: the engine asks for
        /// up to <c>kMaxNative</c> drops when an offer sells, beyond what a ledger amount may hold.
        /// </summary>
        internal static XrplAmount FromUnits(IssuedCurrency asset, AmountKind kind, long units) =>
            new XrplAmount(asset, kind, units < 0 ? (ulong)-units : (ulong)units, 0, units < 0);

        /// <summary>An amount with no asset, for the rates <see cref="XrplQuality"/> works with (<c>noIssue()</c>).</summary>
        internal static XrplAmount Rate(ulong mantissa, int exponent) =>
            FromStParts(null, AmountKind.Iou, mantissa, exponent, false, NumberRounding.ToNearest);

        /// <summary>Throws when two amounts are not of the same asset, as rippled's <c>areComparable</c>.</summary>
        internal static void RequireSameAsset(XrplAmount a, XrplAmount b)
        {
            if (a.Kind != b.Kind || !SameAsset(a._asset, b._asset))
                throw new ArgumentException("The amounts are of different assets.");
        }

        /// <summary>
        /// <c>STAmount(asset, mantissa, exponent, negative)</c> followed by <c>canonicalize()</c>: the
        /// exact value <paramref name="magnitude"/> x 10^<paramref name="exponent"/> rounded once, in
        /// <paramref name="rounding"/>, to what the asset holds.
        /// </summary>
        /// <exception cref="OverflowException">The value is beyond what the asset can hold.</exception>
        internal static XrplAmount Canonical(
            IssuedCurrency asset,
            AmountKind kind,
            bool negative,
            BigInteger magnitude,
            int exponent,
            NumberRounding rounding)
        {
            if (magnitude.IsZero)
                return new XrplAmount(asset, kind, 0, kind == AmountKind.Iou ? IouZeroExponent : 0, false);

            if (kind != AmountKind.Iou)
            {
                // Integral: nothing survives below 10^-20, and an exponent past what the asset can
                // reach is refused before any arithmetic, as rippled does.
                if (exponent <= -20)
                    return new XrplAmount(asset, kind, 0, 0, false);
                if (kind == AmountKind.Xrp && exponent > 17)
                    throw new OverflowException("Native currency amount out of range");
                if (kind == AmountKind.Mpt && exponent > 18)
                    throw new OverflowException("MPT amount out of range");

                BigInteger units = exponent >= 0
                    ? magnitude * BigInteger.Pow(10, exponent)
                    : RoundedQuotient(magnitude, BigInteger.Pow(10, -exponent), negative, rounding);
                if (units > long.MaxValue)
                    throw new OverflowException("Number::operator rep() overflow");

                ulong limit = kind == AmountKind.Xrp ? MaxDrops : MaxMptAmount;
                if (units > limit)
                {
                    throw new OverflowException(kind == AmountKind.Xrp
                        ? "Native currency amount out of range"
                        : "MPT amount out of range");
                }

                return new XrplAmount(asset, kind, (ulong)units, 0, negative);
            }

            // An issued currency: 16 significant digits, then the exponent range.
            int digits = DigitCount(magnitude);
            int shift = digits - IouDigits;
            BigInteger mantissa = shift > 0
                ? RoundedQuotient(magnitude, BigInteger.Pow(10, shift), negative, rounding)
                : magnitude * BigInteger.Pow(10, -shift);
            int finalExponent = exponent + shift;
            if (mantissa > MaxIouMantissa)
            {
                // Rounding carried into a seventeenth digit: 9999999999999999.5 up is 10^16.
                mantissa /= 10;
                finalExponent++;
            }

            if (finalExponent > MaxIouExponent)
                throw new OverflowException("value overflow");
            if (finalExponent < MinIouExponent)
                return new XrplAmount(asset, kind, 0, IouZeroExponent, false);

            return new XrplAmount(asset, kind, (ulong)mantissa, finalExponent, negative);
        }

        /// <summary>
        /// <c>STAmount(asset, mantissa, exponent, negative)</c> exactly as rippled builds it from
        /// its raw fields, including two quirks of the fixed-width types: an issued currency takes
        /// its mantissa through <c>static_cast&lt;std::int64_t&gt;</c>, so a value above
        /// <see cref="long.MaxValue"/> wraps and changes sign; and an XRP or MPT value above it
        /// loses its last digit, truncated, before it is rounded (<c>Number::mantissa()</c>).
        /// </summary>
        /// <exception cref="OverflowException">The value is beyond what the asset can hold.</exception>
        internal static XrplAmount FromStParts(
            IssuedCurrency asset,
            AmountKind kind,
            ulong value,
            int exponent,
            bool negative,
            NumberRounding rounding)
        {
            if (kind == AmountKind.Iou)
            {
                long mantissa = unchecked((long)value);
                if (negative)
                    mantissa = unchecked(-mantissa);

                return mantissa == 0
                    ? new XrplAmount(asset, kind, 0, IouZeroExponent, false)
                    : Canonical(asset, kind, mantissa < 0, BigInteger.Abs(mantissa), exponent, rounding);
            }

            if (value == 0 || exponent <= -20)
                return new XrplAmount(asset, kind, 0, 0, false);
            if (kind == AmountKind.Xrp && exponent > 17)
                throw new OverflowException("Native currency amount out of range");
            if (kind == AmountKind.Mpt && exponent > 18)
                throw new OverflowException("MPT amount out of range");

            if (value > long.MaxValue)
            {
                value /= 10;
                exponent++;
            }

            return Canonical(asset, kind, negative, value, exponent, rounding);
        }

        /// <summary>
        /// <paramref name="value"/> / <paramref name="divisor"/> rounded to an integer in
        /// <paramref name="rounding"/>, the directed modes taking the sign into account.
        /// </summary>
        internal static BigInteger RoundedQuotient(BigInteger value, BigInteger divisor, bool negative, NumberRounding rounding)
        {
            BigInteger quotient = BigInteger.DivRem(value, divisor, out BigInteger remainder);
            if (remainder.IsZero)
                return quotient;

            bool away = rounding switch
            {
                NumberRounding.TowardsZero => false,
                NumberRounding.Upward => !negative,
                NumberRounding.Downward => negative,
                _ => CompareHalf(remainder, divisor, quotient),
            };

            return away ? quotient + 1 : quotient;
        }

        private static bool CompareHalf(BigInteger remainder, BigInteger divisor, BigInteger quotient)
        {
            int half = (remainder * 2).CompareTo(divisor);
            return half > 0 || (half == 0 && !quotient.IsEven);
        }

        private static int DigitCount(BigInteger magnitude) =>
            magnitude.IsZero ? 1 : BigInteger.Abs(magnitude).ToString(CultureInfo.InvariantCulture).Length;

        private static bool TryParseParts(string text, out bool negative, out BigInteger mantissa, out int exponent)
        {
            negative = false;
            mantissa = BigInteger.Zero;
            exponent = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            Match match = AmountText.Match(text.Trim());
            if (!match.Success)
                return false;

            string whole = match.Groups[2].Value;
            string fraction = match.Groups[4].Value;
            string digits = (whole + fraction).TrimStart('0');
            mantissa = digits.Length == 0 ? BigInteger.Zero : BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            negative = match.Groups[1].Value == "-" && !mantissa.IsZero;

            long scale = -fraction.Length;
            if (match.Groups[6].Success && match.Groups[6].Value.Length > 0)
            {
                if (!long.TryParse(match.Groups[6].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long written))
                    return false;
                scale += written;
            }

            if (scale < int.MinValue / 2 || scale > int.MaxValue / 2)
                return false;

            exponent = (int)scale;
            return true;
        }
    }
}
