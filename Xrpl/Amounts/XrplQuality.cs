using System;
using System.Globalization;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

namespace Xrpl.Amounts
{
    /// <summary>
    /// rippled's <c>Quality</c>: the exchange rate of an offer, <c>in / out</c>, in the 64-bit
    /// encoding the ledger sorts order books by - the exponent plus 100 in the top byte, the
    /// 16-digit mantissa below it.
    /// </summary>
    /// <remarks>
    /// A lower encoded value is a better quality: the taker gets more for less. The comparison
    /// operators follow rippled's: <c>a &lt; b</c> means <c>a</c> is the worse quality.
    /// </remarks>
    public readonly struct XrplQuality : IEquatable<XrplQuality>, IComparable<XrplQuality>
    {
        /// <summary>A quality from its 64-bit encoding.</summary>
        public XrplQuality(ulong value)
        {
            Value = value;
        }

        /// <summary>The 64-bit encoding. Zero is the quality of an offer that gives nothing.</summary>
        public ulong Value { get; }

        /// <summary>
        /// The rate <c>in / out</c> the encoding holds (<c>Quality::rate()</c>); zero for the
        /// zero quality.
        /// </summary>
        public XrplNumber Rate => RateAmount.Value;

        /// <summary><c>amountFromQuality</c>: the rate as the unitless amount rippled computes with.</summary>
        internal XrplAmount RateAmount
        {
            get
            {
                if (Value == 0)
                    return XrplAmount.Rate(0, 0);

                ulong mantissa = Value & ~(255UL << 56);
                int exponent = (int)(Value >> 56) - 100;
                return XrplAmount.Rate(mantissa, exponent);
            }
        }

        /// <summary>
        /// The quality of an offer that takes <paramref name="in"/> and gives <paramref name="out"/>
        /// (<c>Quality(Amounts)</c>, <c>getRate(out, in)</c>).
        /// </summary>
        public static XrplQuality FromAmounts(XrplAmount @in, XrplAmount @out, LedgerRules rules = null) =>
            new XrplQuality(GetRate(@out, @in, XrplAmountMath.Context(rules).Rounding));

        /// <summary>
        /// The quality an order book directory is sorted by: the last 16 hex digits of its index,
        /// the <c>BookDirectory</c> of an offer. rippled crosses an offer at this quality, set
        /// when the offer was placed, not at the ratio of what the offer has left.
        /// </summary>
        /// <exception cref="FormatException">The text is not a 64-character hex index.</exception>
        public static XrplQuality FromBookDirectory(string bookDirectory)
        {
            if (bookDirectory == null || bookDirectory.Length != 64)
                throw new FormatException("A BookDirectory is 64 hex characters.");

            if (!ulong.TryParse(bookDirectory.Substring(48), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
                throw new FormatException($"'{bookDirectory}' is not a hex index.");

            return new XrplQuality(value);
        }

        /// <summary>
        /// <c>Quality::ceilIn</c>: the offer's amounts cut down so it takes at most
        /// <paramref name="limit"/>, the output recomputed at this quality with the legacy
        /// <see cref="XrplAmountMath.DivRound"/>, rounded up, and never above what the offer gives.
        /// </summary>
        public (XrplAmount In, XrplAmount Out) CeilIn(XrplAmount @in, XrplAmount @out, XrplAmount limit, LedgerRules rules = null) =>
            CeilIn(@in, @out, limit, roundUp: true, strict: false, rules);

        /// <summary>
        /// <c>Quality::ceilInStrict</c>: <see cref="CeilIn"/> with <see cref="XrplAmountMath.DivRoundStrict"/>
        /// in the requested direction.
        /// </summary>
        public (XrplAmount In, XrplAmount Out) CeilInStrict(XrplAmount @in, XrplAmount @out, XrplAmount limit, bool roundUp, LedgerRules rules = null) =>
            CeilIn(@in, @out, limit, roundUp, strict: true, rules);

        /// <summary>
        /// <c>Quality::ceilOut</c>: the offer's amounts cut down so it gives at most
        /// <paramref name="limit"/>, the input recomputed at this quality with the legacy
        /// <see cref="XrplAmountMath.MulRound"/>, rounded up, and never above what the offer takes.
        /// </summary>
        public (XrplAmount In, XrplAmount Out) CeilOut(XrplAmount @in, XrplAmount @out, XrplAmount limit, LedgerRules rules = null) =>
            CeilOut(@in, @out, limit, roundUp: true, strict: false, rules);

        /// <summary>
        /// <c>Quality::ceilOutStrict</c>: <see cref="CeilOut"/> with <see cref="XrplAmountMath.MulRoundStrict"/>
        /// in the requested direction.
        /// </summary>
        public (XrplAmount In, XrplAmount Out) CeilOutStrict(XrplAmount @in, XrplAmount @out, XrplAmount limit, bool roundUp, LedgerRules rules = null) =>
            CeilOut(@in, @out, limit, roundUp, strict: true, rules);

        /// <summary>
        /// <c>Quality::round</c>: the quality with its mantissa rounded up, to a worse quality, to
        /// <paramref name="digits"/> significant digits - the rounding a tick size applies to an offer.
        /// </summary>
        /// <param name="digits">The significant digits to keep, 1 to 16; 16 leaves the quality as it is.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="digits"/> is outside 1 to 16.</exception>
        public XrplQuality Round(int digits)
        {
            if (digits < 1 || digits > 16)
                throw new ArgumentOutOfRangeException(nameof(digits), digits, "A quality keeps 1 to 16 significant digits.");

            ulong modulus = 1;
            for (int i = digits; i < 16; i++)
                modulus *= 10;

            ulong exponent = Value >> 56;
            ulong mantissa = Value & 0x00ffffffffffffffUL;
            mantissa += modulus - 1;
            mantissa -= mantissa % modulus;
            return new XrplQuality((exponent << 56) | mantissa);
        }

        /// <summary>
        /// Whether two qualities are within a relative distance of each other, as rippled's
        /// <c>withinRelativeDistance(Quality, Quality, Number)</c> decides it from their rates.
        /// </summary>
        public static bool WithinRelativeDistance(XrplQuality calculated, XrplQuality requested, XrplNumber distance, LedgerRules rules = null)
        {
            if (calculated == requested)
                return true;

            // min is the worse quality: the larger encoding, the larger rate.
            XrplQuality worse = calculated < requested ? calculated : requested;
            XrplQuality better = calculated < requested ? requested : calculated;
            XrplAmount minRate = worse.RateAmount;
            XrplAmount difference = XrplAmountMath.Subtract(minRate, better.RateAmount, rules);
            NumberContext c = XrplAmountMath.Context(rules);
            return XrplNumber.Divide(difference.Value, minRate.Value, c) < distance;
        }

        /// <summary><c>getRate(offerOut, offerIn)</c>: zero when the output is zero or the division fails.</summary>
        internal static ulong GetRate(XrplAmount offerOut, XrplAmount offerIn, NumberRounding ambient)
        {
            if (offerOut.IsZero)
                return 0;

            try
            {
                XrplAmount rate = XrplAmountMath.DivideInto(offerIn, offerOut, null, AmountKind.Iou, ambient);
                if (rate.IsZero)
                    return 0;

                ulong exponent = (ulong)(rate.StExponent + 100);
                return (exponent << 56) | rate.StMantissa;
            }
            catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
            {
                return 0;
            }
        }

        private (XrplAmount In, XrplAmount Out) CeilIn(XrplAmount @in, XrplAmount @out, XrplAmount limit, bool roundUp, bool strict, LedgerRules rules)
        {
            if (@in <= limit)
                return (@in, @out);

            XrplAmount rate = RateAmount;
            XrplAmount resultOut = XrplAmountMath.DivRoundImpl(limit, rate, @out.Asset, @out.Kind, roundUp, strict, rules);
            if (resultOut > @out)
                resultOut = @out;

            return (limit, resultOut);
        }

        private (XrplAmount In, XrplAmount Out) CeilOut(XrplAmount @in, XrplAmount @out, XrplAmount limit, bool roundUp, bool strict, LedgerRules rules)
        {
            if (@out <= limit)
                return (@in, @out);

            XrplAmount rate = RateAmount;
            XrplAmount resultIn = XrplAmountMath.MulRoundImpl(limit, rate, @in.Asset, @in.Kind, roundUp, strict, rules);
            if (resultIn > @in)
                resultIn = @in;

            return (resultIn, limit);
        }

        /// <inheritdoc />
        public bool Equals(XrplQuality other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is XrplQuality other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Orders qualities from worse to better: a better quality compares greater.</summary>
        public int CompareTo(XrplQuality other) => other.Value.CompareTo(Value);

        /// <summary>The encoding in hex, as rippled logs it.</summary>
        public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);

        public static bool operator ==(XrplQuality left, XrplQuality right) => left.Value == right.Value;

        public static bool operator !=(XrplQuality left, XrplQuality right) => left.Value != right.Value;

        /// <summary><paramref name="left"/> is the worse quality.</summary>
        public static bool operator <(XrplQuality left, XrplQuality right) => left.Value > right.Value;

        /// <summary><paramref name="left"/> is the better quality.</summary>
        public static bool operator >(XrplQuality left, XrplQuality right) => left.Value < right.Value;

        public static bool operator <=(XrplQuality left, XrplQuality right) => left.Value >= right.Value;

        public static bool operator >=(XrplQuality left, XrplQuality right) => left.Value <= right.Value;
    }
}
