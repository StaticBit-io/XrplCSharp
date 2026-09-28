using System;
using System.Globalization;
using System.Numerics;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// What one offer contributes when a payment or an <c>OfferCreate</c> crosses it, sized the
    /// way rippled's <c>BookStep</c> sizes it: the offer's funding, transfer fees in and out, and
    /// the cut to an input or output limit, each rounded as the node rounds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A step starts from <see cref="Fund"/>, which applies the transfer rates and what the offer's
    /// owner can actually pay. <see cref="LimitStepIn"/> and <see cref="LimitStepOut"/> then cut it
    /// to what the rest of the path allows, as <c>BookStep</c>'s forward and reverse passes do.
    /// <see cref="PassesQualityLimit"/> is the check a strand's result must pass against the
    /// taker's limit quality.
    /// </para>
    /// <para>
    /// The offer is crossed at the quality of its book directory (<see cref="XrplQuality.FromBookDirectory"/>),
    /// set when it was placed, not at the ratio of what it has left.
    /// </para>
    /// </remarks>
    public static class OfferCrossing
    {
        /// <summary>A transfer rate of 1: no fee (<c>QUALITY_ONE</c>).</summary>
        public const uint QualityOne = 1_000_000_000;

        /// <summary>
        /// <c>TOffer::limitIn</c>: the offer cut to take at most <paramref name="limit"/>, with
        /// <c>ceilInStrict</c> under <c>fixReducedOffersV2</c> and <c>ceilIn</c> before it.
        /// </summary>
        public static (XrplAmount In, XrplAmount Out) LimitIn(
            XrplQuality quality,
            XrplAmount offerIn,
            XrplAmount offerOut,
            XrplAmount limit,
            bool roundUp,
            LedgerRules rules = null)
        {
            return (rules ?? new LedgerRules()).FixReducedOffersV2
                ? quality.CeilInStrict(offerIn, offerOut, limit, roundUp, rules)
                : quality.CeilIn(offerIn, offerOut, limit, rules);
        }

        /// <summary><c>TOffer::limitOut</c>: the offer cut to give at most <paramref name="limit"/>, with <c>ceilOutStrict</c>.</summary>
        public static (XrplAmount In, XrplAmount Out) LimitOut(
            XrplQuality quality,
            XrplAmount offerIn,
            XrplAmount offerOut,
            XrplAmount limit,
            bool roundUp,
            LedgerRules rules = null) =>
            quality.CeilOutStrict(offerIn, offerOut, limit, roundUp, rules);

        /// <summary>
        /// <c>mulRatio(amount, num, den, roundUp)</c>: the amount times <c>num / den</c>, as the
        /// typed amounts compute it - exact with a directed last unit for XRP and MPT, a 64-bit
        /// mantissa with the remainder deciding the last unit for an issued currency.
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="den"/> is zero.</exception>
        /// <exception cref="OverflowException">The result is beyond what the asset can hold.</exception>
        public static XrplAmount MulRatio(XrplAmount amount, uint num, uint den, bool roundUp, LedgerRules rules = null)
        {
            if (den == 0)
                throw new DivideByZeroException("division by zero");

            return amount.IsIntegral
                ? MulRatioIntegral(amount, num, den, roundUp)
                : MulRatioIou(amount, num, den, roundUp, XrplAmountMath.Context(rules).Rounding);
        }

        /// <summary>
        /// What an offer can contribute before any limit: <c>BookStep::forEachOffer</c>. The input
        /// carries the transfer fee on the way in; the owner pays the fee on the way out, and an
        /// owner with less than that is cut to what they hold.
        /// </summary>
        /// <param name="quality">The offer's quality, from its book directory.</param>
        /// <param name="offerIn">What the offer takes: its <c>TakerPays</c>.</param>
        /// <param name="offerOut">What the offer gives: its <c>TakerGets</c>.</param>
        /// <param name="ownerFunds">
        /// What the owner holds of the output asset; null when the owner issues it, and so has
        /// unlimited funds.
        /// </param>
        /// <param name="transferRateIn">The input's transfer rate, when the previous step redeems; otherwise <see cref="QualityOne"/>.</param>
        /// <param name="transferRateOut">The output's transfer rate, when the owner pays it; otherwise <see cref="QualityOne"/>.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static OfferStep Fund(
            XrplQuality quality,
            XrplAmount offerIn,
            XrplAmount offerOut,
            XrplAmount? ownerFunds,
            uint transferRateIn = QualityOne,
            uint transferRateOut = QualityOne,
            LedgerRules rules = null)
        {
            XrplAmount stepIn = MulRatio(offerIn, transferRateIn, QualityOne, roundUp: true, rules);
            XrplAmount stepOut = offerOut;
            XrplAmount ownerGives = MulRatio(offerOut, transferRateOut, QualityOne, roundUp: offerOut.Kind == AmountKind.Mpt, rules);

            XrplAmount funds = ownerFunds ?? ownerGives;
            if (funds < ownerGives)
            {
                ownerGives = funds;
                stepOut = MulRatio(ownerGives, QualityOne, transferRateOut, roundUp: false, rules);

                // Rounded down, strictly, so the offer left in the book is no worse than its page.
                (offerIn, offerOut) = LimitOut(quality, offerIn, offerOut, stepOut, roundUp: false, rules);
                stepIn = MulRatio(offerIn, transferRateIn, QualityOne, roundUp: true, rules);
            }

            return new OfferStep(quality, offerIn, offerOut, stepIn, stepOut, ownerGives, transferRateIn, transferRateOut);
        }

        /// <summary>
        /// <c>limitStepIn</c>: the step cut to take at most <paramref name="limit"/>, the offer's
        /// share found with <see cref="LimitIn"/> rounded down, as the forward pass does.
        /// </summary>
        public static OfferStep LimitStepIn(OfferStep step, XrplAmount limit, LedgerRules rules = null)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (!(limit < step.StepIn))
                return step;

            XrplAmount inLimit = MulRatio(limit, QualityOne, step.TransferRateIn, roundUp: false, rules);
            (XrplAmount offerIn, XrplAmount offerOut) = LimitIn(step.Quality, step.OfferIn, step.OfferOut, inLimit, roundUp: false, rules);
            XrplAmount ownerGives = MulRatio(offerOut, step.TransferRateOut, QualityOne, roundUp: offerOut.Kind == AmountKind.Mpt, rules);
            return new OfferStep(step.Quality, offerIn, offerOut, limit, offerOut, ownerGives, step.TransferRateIn, step.TransferRateOut);
        }

        /// <summary>
        /// <c>limitStepOut</c>: the step cut to give at most <paramref name="limit"/>, the offer's
        /// share found with <see cref="LimitOut"/> rounded up, as the reverse pass does.
        /// </summary>
        public static OfferStep LimitStepOut(OfferStep step, XrplAmount limit, LedgerRules rules = null)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (!(limit < step.StepOut))
                return step;

            XrplAmount ownerGives = MulRatio(limit, step.TransferRateOut, QualityOne, roundUp: limit.Kind == AmountKind.Mpt, rules);
            (XrplAmount offerIn, XrplAmount offerOut) = LimitOut(step.Quality, step.OfferIn, step.OfferOut, limit, roundUp: true, rules);
            XrplAmount stepIn = MulRatio(offerIn, step.TransferRateIn, QualityOne, roundUp: true, rules);
            return new OfferStep(step.Quality, offerIn, offerOut, stepIn, limit, ownerGives, step.TransferRateIn, step.TransferRateOut);
        }

        /// <summary>
        /// The forward pass over one offer, <c>BookStep::fwdImp</c>: the step fed
        /// <paramref name="input"/>, cut with <see cref="LimitStepIn"/> when that is less than the
        /// offer takes. When the result delivers more than the reverse pass asked for on no more
        /// input, the node recomputes the input for the reverse pass's output and, if it is the
        /// same, settles on that output - so a crossing delivers exactly what was asked for.
        /// </summary>
        /// <param name="funded">The offer's step from <see cref="Fund"/>.</param>
        /// <param name="input">What reaches the step on the forward pass.</param>
        /// <param name="reverse">The same offer's step from the reverse pass.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static OfferStep ForwardPass(OfferStep funded, XrplAmount input, OfferStep reverse, LedgerRules rules = null)
        {
            if (funded == null)
                throw new ArgumentNullException(nameof(funded));
            if (reverse == null)
                throw new ArgumentNullException(nameof(reverse));

            OfferStep step = funded.StepIn <= input ? funded : LimitStepIn(funded, input, rules);
            XrplAmount consumed = funded.StepIn <= input ? funded.StepIn : input;
            if (!(step.StepOut > reverse.StepOut) || consumed > reverse.StepIn)
                return step;

            OfferStep settled = LimitStepOut(funded, reverse.StepOut, rules);
            if (settled.StepIn != consumed)
                return step;

            return new OfferStep(
                funded.Quality,
                settled.OfferIn,
                settled.OfferOut,
                consumed,
                reverse.StepOut,
                settled.OwnerGives,
                funded.TransferRateIn,
                funded.TransferRateOut);
        }

        /// <summary>
        /// What crossing one offer delivers when a taker asks for <paramref name="deliver"/> and
        /// pays at most <paramref name="sendMax"/>: the reverse pass sizes the step to the output,
        /// the forward pass runs from the input that needs (capped at <paramref name="sendMax"/>),
        /// as a strand made of this one book step is run.
        /// </summary>
        /// <param name="funded">The offer's step from <see cref="Fund"/>.</param>
        /// <param name="deliver">The most the taker asks for.</param>
        /// <param name="sendMax">The most the taker pays.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static OfferStep Cross(OfferStep funded, XrplAmount deliver, XrplAmount sendMax, LedgerRules rules = null)
        {
            OfferStep reverse = LimitStepOut(funded, deliver, rules);
            XrplAmount input = reverse.StepIn < sendMax ? reverse.StepIn : sendMax;
            return ForwardPass(funded, input, reverse, rules);
        }

        /// <summary>
        /// The check a strand's result must pass: its quality, <c>Quality(out, in)</c>, no worse
        /// than <paramref name="limitQuality"/> - or, when the requested output was adjusted to
        /// reach that quality, within a relative 10^-7 of it.
        /// </summary>
        public static bool PassesQualityLimit(
            XrplAmount stepIn,
            XrplAmount stepOut,
            XrplQuality limitQuality,
            bool adjustedRemainingOut = false,
            LedgerRules rules = null)
        {
            XrplQuality quality = XrplQuality.FromAmounts(stepIn, stepOut, rules);
            if (!(quality < limitQuality))
                return true;

            return adjustedRemainingOut &&
                   XrplQuality.WithinRelativeDistance(quality, limitQuality, new XrplNumber(1, -7), rules);
        }

        private static XrplAmount MulRatioIntegral(XrplAmount amount, uint num, uint den, bool roundUp)
        {
            bool negative = amount.IsNegative;
            BigInteger product = (negative ? -(BigInteger)amount.StMantissa : amount.StMantissa) * num;
            BigInteger result = BigInteger.DivRem(product, den, out BigInteger remainder);
            if (!remainder.IsZero)
            {
                if (!negative && roundUp)
                    result += 1;
                if (negative && !roundUp)
                    result -= 1;
            }

            if (result > long.MaxValue)
                throw new OverflowException(amount.Kind == AmountKind.Xrp ? "XRP mulRatio overflow" : "MPT mulRatio overflow");

            return XrplAmount.Canonical(amount.Asset, amount.Kind, result.Sign < 0, BigInteger.Abs(result), 0, NumberRounding.ToNearest);
        }

        /// <summary><c>mulRatio(IOUAmount, ...)</c>, step for step.</summary>
        private static XrplAmount MulRatioIou(XrplAmount amount, uint num, uint den, bool roundUp, NumberRounding ambient)
        {
            const int log10OfInt64Max = 18;
            bool negative = amount.IsNegative;
            BigInteger product = (BigInteger)amount.StMantissa * num;
            BigInteger low = BigInteger.DivRem(product, den, out BigInteger remainder);
            int exponent = amount.IsZero ? XrplAmount.IouZeroExponent : amount.StExponent;

            if (!remainder.IsZero)
            {
                // Scale up so the quotient keeps as many digits as a 64-bit mantissa holds.
                int roomToGrow = log10OfInt64Max - Log10Ceil(low);
                if (roomToGrow > 0)
                {
                    exponent -= roomToGrow;
                    BigInteger scale = BigInteger.Pow(10, roomToGrow);
                    low *= scale;
                    remainder *= scale;
                }

                BigInteger addRemainder = remainder / den;
                low += addRemainder;
                remainder -= addRemainder * den;
            }

            bool hasRemainder = !remainder.IsZero;
            int mustShrink = Log10Ceil(low) - log10OfInt64Max;
            if (mustShrink > 0)
            {
                BigInteger saved = low;
                BigInteger scale = BigInteger.Pow(10, mustShrink);
                exponent += mustShrink;
                low /= scale;
                if (!hasRemainder)
                    hasRemainder = saved != low * scale;
            }

            XrplAmount result = IouAmount(amount.Asset, negative, low, exponent, ambient);
            if (!hasRemainder)
                return result;

            if (roundUp && !negative)
            {
                return result.IsZero
                    ? IouAmount(amount.Asset, false, XrplAmount.MinIouMantissa, XrplAmount.MinIouExponent, ambient)
                    : IouAmount(amount.Asset, false, (BigInteger)result.StMantissa + 1, result.StExponent, ambient);
            }

            if (!roundUp && negative)
            {
                return result.IsZero
                    ? IouAmount(amount.Asset, true, XrplAmount.MinIouMantissa, XrplAmount.MinIouExponent, ambient)
                    : IouAmount(amount.Asset, true, (BigInteger)result.StMantissa + 1, result.StExponent, ambient);
            }

            return result;
        }

        /// <summary><c>IOUAmount(mantissa, exponent)</c>: normalized in the ambient mode.</summary>
        private static XrplAmount IouAmount(IssuedCurrency asset, bool negative, BigInteger magnitude, int exponent, NumberRounding ambient) =>
            XrplAmount.Canonical(asset, AmountKind.Iou, negative, magnitude, exponent, ambient);

        /// <summary><c>ceil(log10(value))</c> over the powers of ten, as rippled's table lookup: 0 for 0 and 1.</summary>
        private static int Log10Ceil(BigInteger value)
        {
            int index = 0;
            BigInteger power = BigInteger.One;
            while (power < value)
            {
                power *= 10;
                index++;
            }

            return index;
        }
    }

    /// <summary>What one offer contributes to a step of a payment or an offer crossing.</summary>
    public sealed class OfferStep
    {
        internal OfferStep(
            XrplQuality quality,
            XrplAmount offerIn,
            XrplAmount offerOut,
            XrplAmount stepIn,
            XrplAmount stepOut,
            XrplAmount ownerGives,
            uint transferRateIn,
            uint transferRateOut)
        {
            Quality = quality;
            OfferIn = offerIn;
            OfferOut = offerOut;
            StepIn = stepIn;
            StepOut = stepOut;
            OwnerGives = ownerGives;
            TransferRateIn = transferRateIn;
            TransferRateOut = transferRateOut;
        }

        /// <summary>The offer's quality, from its book directory.</summary>
        public XrplQuality Quality { get; }

        /// <summary>What the offer takes: the part of its <c>TakerPays</c> this step consumes.</summary>
        public XrplAmount OfferIn { get; }

        /// <summary>What the offer gives: the part of its <c>TakerGets</c> this step consumes.</summary>
        public XrplAmount OfferOut { get; }

        /// <summary>What the step takes in, the input transfer fee included.</summary>
        public XrplAmount StepIn { get; }

        /// <summary>What the step delivers.</summary>
        public XrplAmount StepOut { get; }

        /// <summary>What the offer's owner pays, the output transfer fee included.</summary>
        public XrplAmount OwnerGives { get; }

        /// <summary>The input's transfer rate, in billionths; <see cref="OfferCrossing.QualityOne"/> for none.</summary>
        public uint TransferRateIn { get; }

        /// <summary>The output's transfer rate, in billionths; <see cref="OfferCrossing.QualityOne"/> for none.</summary>
        public uint TransferRateOut { get; }

        /// <inheritdoc />
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "offer {0} -> {1}, step {2} -> {3}", OfferIn, OfferOut, StepIn, StepOut);
    }
}
