using System;
using System.Numerics;

using Xrpl.BinaryCodec.Numbers;

namespace Xrpl.Sugar
{
    /// <summary>
    /// rippled's <c>roundToAsset</c>: a <c>Number</c> rounded to what an amount of the asset can
    /// hold, computed the way <c>STAmount</c> computes it.
    /// </summary>
    internal static class AssetRounding
    {
        /// <summary>Significant digits of an IOU amount: its mantissa lies in [10^15, 10^16).</summary>
        private const int IouDigits = 16;

        /// <summary>
        /// <c>roundToAsset(asset, value, scale, rounding)</c>. XRP and MPT amounts become whole units
        /// (<c>STAmount::fromNumber</c> converts the magnitude to an integer). An IOU amount is
        /// rounded to 16 significant digits, then to a multiple of 10^<paramref name="scale"/>
        /// through <c>roundToScale</c>'s <c>(value + reference) - reference</c>.
        /// </summary>
        /// <param name="value">The value to round.</param>
        /// <param name="integral">Whether the asset is XRP or an MPT.</param>
        /// <param name="scale">The loan's <c>LoanScale</c>; ignored for XRP and MPT.</param>
        /// <param name="context">The ledger's scale, with the rounding mode to apply.</param>
        internal static XrplNumber Round(XrplNumber value, bool integral, int scale, NumberContext context)
        {
            if (value.IsZero)
                return value;

            if (integral)
            {
                long whole = XrplNumber.Abs(value).ToInt64(context);
                return value.IsNegative ? -(XrplNumber)whole : (XrplNumber)whole;
            }

            // STAmount::fromNumber rounds the magnitude and puts the sign back.
            XrplNumber magnitude = RoundToIouDigits(XrplNumber.Abs(value), context.Rounding);
            XrplNumber amount = value.IsNegative ? -magnitude : magnitude;
            if (IouExponent(amount) >= scale)
                return amount;

            // roundToScale: STAmount addition is Number addition followed by IOUAmount's signed
            // normalization to 16 digits, both in the requested mode.
            XrplNumber reference = new XrplNumber(amount.IsNegative ? -1_000_000_000_000_000L : 1_000_000_000_000_000L, scale);
            XrplNumber sum = RoundToIouDigits(XrplNumber.Add(amount, reference, context), context.Rounding);
            return RoundToIouDigits(XrplNumber.Subtract(sum, reference, context), context.Rounding);
        }

        /// <summary>
        /// <c>Number::normalizeToRange&lt;10^15, 10^16 - 1&gt;</c>: the value rounded to 16 significant
        /// digits in <paramref name="rounding"/>, directed modes taking the sign into account.
        /// </summary>
        internal static XrplNumber RoundToIouDigits(XrplNumber value, NumberRounding rounding)
        {
            if (value.IsZero)
                return value;

            BigInteger magnitude = BigInteger.Abs(value.Mantissa);
            int dropped = DigitCount(magnitude) - IouDigits;
            if (dropped <= 0)
                return value;

            BigInteger divisor = BigInteger.Pow(10, dropped);
            BigInteger kept = BigInteger.DivRem(magnitude, divisor, out BigInteger remainder);
            if (RoundsAway(value.IsNegative, kept, remainder, divisor, rounding))
                kept += 1;

            long mantissa = (long)kept;
            return new XrplNumber(value.IsNegative ? -mantissa : mantissa, value.Exponent + dropped);
        }

        /// <summary>The exponent of an IOU amount: the one that puts its mantissa in [10^15, 10^16).</summary>
        private static int IouExponent(XrplNumber amount) =>
            amount.Exponent + DigitCount(BigInteger.Abs(amount.Mantissa)) - IouDigits;

        private static bool RoundsAway(bool negative, BigInteger kept, BigInteger remainder, BigInteger divisor, NumberRounding rounding)
        {
            if (remainder.IsZero)
                return false;

            switch (rounding)
            {
                case NumberRounding.ToNearest:
                    int half = (remainder * 2).CompareTo(divisor);
                    return half > 0 || (half == 0 && !kept.IsEven);
                case NumberRounding.Upward:
                    return !negative;
                case NumberRounding.Downward:
                    return negative;
                case NumberRounding.TowardsZero:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rounding));
            }
        }

        private static int DigitCount(BigInteger magnitude)
        {
            int digits = 1;
            for (BigInteger bound = 10; magnitude >= bound; bound *= 10)
                digits++;

            return digits;
        }
    }
}
