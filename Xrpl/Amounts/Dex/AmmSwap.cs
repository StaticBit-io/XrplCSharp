using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

namespace Xrpl.Amounts
{
    /// <summary>
    /// The swap equations of rippled's <c>AMMHelpers.h</c> the payment engine sizes AMM offers
    /// with: <c>swapAssetIn</c>, <c>swapAssetOut</c> and <c>changeSpotPriceQuality</c>, each
    /// with the rounding <c>fixAMMv1_1</c> sets step by step.
    /// </summary>
    internal static class AmmSwap
    {
        private static readonly XrplNumber ReducedOfferPct = new XrplNumber(9999, -4);

        /// <summary><c>swapAssetIn</c>: what the pool pays out for <paramref name="assetIn"/>.</summary>
        internal static XrplAmount SwapAssetIn(XrplAmount poolIn, XrplAmount poolOut, XrplAmount assetIn, ushort tradingFee, LedgerRules rules)
        {
            rules ??= new LedgerRules();
            NumberContext c = rules.Context;
            if (!rules.FixAMMv1_1)
            {
                XrplNumber legacy = XrplNumber.Subtract(
                    poolOut.Value,
                    XrplNumber.Divide(
                        XrplNumber.Multiply(poolIn.Value, poolOut.Value, c),
                        XrplNumber.Add(poolIn.Value, XrplNumber.Multiply(assetIn.Value, AmmFormulas.FeeMult(tradingFee, c), c), c),
                        c),
                    c);
                return StepMath.ToAmount(poolOut.Asset, legacy, NumberRounding.Downward, c.Rounding);
            }

            NumberContext up = c.WithRounding(NumberRounding.Upward);
            NumberContext down = c.WithRounding(NumberRounding.Downward);

            XrplNumber numerator = XrplNumber.Multiply(poolIn.Value, poolOut.Value, up);
            XrplNumber fee = AmmFormulas.Fee(tradingFee, up);

            XrplNumber denominator = XrplNumber.Add(
                poolIn.Value,
                XrplNumber.Multiply(assetIn.Value, XrplNumber.Subtract(1, fee, down), down),
                down);
            if (denominator.Sign <= 0)
                return XrplAmount.Zero(poolOut.Asset);

            XrplNumber ratio = XrplNumber.Divide(numerator, denominator, up);
            XrplNumber swapOut = XrplNumber.Subtract(poolOut.Value, ratio, down);
            if (swapOut.Sign < 0)
                return XrplAmount.Zero(poolOut.Asset);

            return StepMath.ToAmount(poolOut.Asset, swapOut, NumberRounding.Downward, NumberRounding.Downward);
        }

        /// <summary><c>swapAssetOut</c>: what the pool takes in to pay out <paramref name="assetOut"/>.</summary>
        internal static XrplAmount SwapAssetOut(XrplAmount poolIn, XrplAmount poolOut, XrplAmount assetOut, ushort tradingFee, LedgerRules rules)
        {
            rules ??= new LedgerRules();
            NumberContext c = rules.Context;
            if (!rules.FixAMMv1_1)
            {
                XrplNumber legacy = XrplNumber.Divide(
                    XrplNumber.Subtract(
                        XrplNumber.Divide(
                            XrplNumber.Multiply(poolIn.Value, poolOut.Value, c),
                            StepMath.Subtract(poolOut, assetOut, rules).Value,
                            c),
                        poolIn.Value,
                        c),
                    AmmFormulas.FeeMult(tradingFee, c),
                    c);
                return StepMath.ToAmount(poolIn.Asset, legacy, NumberRounding.Upward, c.Rounding);
            }

            NumberContext up = c.WithRounding(NumberRounding.Upward);
            NumberContext down = c.WithRounding(NumberRounding.Downward);

            XrplNumber numerator = XrplNumber.Multiply(poolIn.Value, poolOut.Value, up);

            // Two amounts of the pool's type: the difference is itself such an amount, rounded
            // to 16 digits for an issued currency, before it becomes a Number.
            XrplNumber denominator = StepMath.Subtract(poolOut, assetOut, rules.WithRounding(NumberRounding.Downward)).Value;
            if (denominator.Sign <= 0)
                return StepMath.MaxAmount(poolIn.Asset);

            XrplNumber ratio = XrplNumber.Divide(numerator, denominator, up);
            XrplNumber numerator2 = XrplNumber.Subtract(ratio, poolIn.Value, up);
            XrplNumber fee = AmmFormulas.Fee(tradingFee, up);

            XrplNumber feeMult = XrplNumber.Subtract(1, fee, down);

            XrplNumber swapIn = XrplNumber.Divide(numerator2, feeMult, up);
            if (swapIn.Sign < 0)
                return XrplAmount.Zero(poolIn.Asset);

            return StepMath.ToAmount(poolIn.Asset, swapIn, NumberRounding.Upward, NumberRounding.Upward);
        }

        /// <summary>
        /// <c>changeSpotPriceQuality</c>: the offer that brings the pool's spot price to
        /// <paramref name="quality"/>, or that has that quality itself; null when neither can be
        /// generated.
        /// </summary>
        internal static (XrplAmount In, XrplAmount Out)? ChangeSpotPriceQuality(
            XrplAmount poolIn,
            XrplAmount poolOut,
            XrplQuality quality,
            ushort tradingFee,
            LedgerRules rules)
        {
            rules ??= new LedgerRules();
            if (!rules.FixAMMv1_1)
                return ChangeSpotPriceQualityLegacy(poolIn, poolOut, quality, tradingFee, rules);

            bool inIntegral = poolIn.IsIntegral;
            bool outIntegral = poolOut.IsIntegral;
            (XrplAmount In, XrplAmount Out)? amounts =
                outIntegral && (!inIntegral || quality.Rate >= 1)
                    ? StartWithTakerGets(poolIn, poolOut, quality, tradingFee, rules)
                    : StartWithTakerPays(poolIn, poolOut, quality, tradingFee, rules);
            if (amounts == null)
                return null;

            if (XrplQuality.FromAmounts(amounts.Value.In, amounts.Value.Out, rules) < quality)
                return null;

            return amounts;
        }

        /// <summary><c>getAMMOfferStartWithTakerGets</c>.</summary>
        private static (XrplAmount In, XrplAmount Out)? StartWithTakerGets(
            XrplAmount poolIn,
            XrplAmount poolOut,
            XrplQuality target,
            ushort tradingFee,
            LedgerRules rules)
        {
            XrplNumber rate = target.Rate;
            if (rate.IsZero)
                return null;

            NumberContext c = rules.Context.WithRounding(NumberRounding.ToNearest);
            XrplNumber f = AmmFormulas.FeeMult(tradingFee, c);
            XrplNumber a = 1;
            XrplNumber b = XrplNumber.Subtract(
                XrplNumber.Divide(
                    XrplNumber.Multiply(poolIn.Value, XrplNumber.Subtract(1, XrplNumber.Divide(1, f, c), c), c),
                    rate,
                    c),
                TypedTimes(poolOut, 2, c),
                c);
            XrplNumber cc = XrplNumber.Subtract(
                XrplNumber.Multiply(poolOut.Value, poolOut.Value, c),
                XrplNumber.Divide(XrplNumber.Multiply(poolIn.Value, poolOut.Value, c), rate, c),
                c);

            XrplNumber? takerGets = SolveQuadraticSmallest(a, b, cc, c);
            if (takerGets == null || takerGets.Value <= XrplNumber.Zero)
                return null;

            XrplNumber constraint = XrplNumber.Subtract(
                poolOut.Value,
                XrplNumber.Divide(poolIn.Value, XrplNumber.Multiply(rate, f, c), c),
                c);
            if (constraint <= XrplNumber.Zero)
                return null;

            XrplNumber proposed = constraint < takerGets.Value ? constraint : takerGets.Value;

            (XrplAmount In, XrplAmount Out) Amounts(XrplNumber gets)
            {
                XrplAmount outAmount = StepMath.ToAmount(poolOut.Asset, gets, NumberRounding.Downward, NumberRounding.ToNearest);
                return (SwapAssetOut(poolIn, poolOut, outAmount, tradingFee, rules), outAmount);
            }

            (XrplAmount In, XrplAmount Out) amounts = Amounts(proposed);
            if (XrplQuality.FromAmounts(amounts.In, amounts.Out, rules) < target)
                return Amounts(ReduceOffer(amounts.Out, c));

            return amounts;
        }

        /// <summary><c>getAMMOfferStartWithTakerPays</c>.</summary>
        private static (XrplAmount In, XrplAmount Out)? StartWithTakerPays(
            XrplAmount poolIn,
            XrplAmount poolOut,
            XrplQuality target,
            ushort tradingFee,
            LedgerRules rules)
        {
            XrplNumber rate = target.Rate;
            if (rate.IsZero)
                return null;

            NumberContext c = rules.Context.WithRounding(NumberRounding.ToNearest);
            XrplNumber f = AmmFormulas.FeeMult(tradingFee, c);
            XrplNumber a = f;
            XrplNumber b = XrplNumber.Multiply(poolIn.Value, XrplNumber.Add(1, f, c), c);
            XrplNumber cc = XrplNumber.Subtract(
                XrplNumber.Multiply(poolIn.Value, poolIn.Value, c),
                XrplNumber.Multiply(XrplNumber.Multiply(poolIn.Value, poolOut.Value, c), rate, c),
                c);

            XrplNumber? takerPays = SolveQuadraticSmallest(a, b, cc, c);
            if (takerPays == null || takerPays.Value <= XrplNumber.Zero)
                return null;

            XrplNumber constraint = XrplNumber.Subtract(
                XrplNumber.Multiply(poolOut.Value, rate, c),
                XrplNumber.Divide(poolIn.Value, f, c),
                c);
            if (constraint <= XrplNumber.Zero)
                return null;

            XrplNumber proposed = constraint < takerPays.Value ? constraint : takerPays.Value;

            (XrplAmount In, XrplAmount Out) Amounts(XrplNumber pays)
            {
                XrplAmount inAmount = StepMath.ToAmount(poolIn.Asset, pays, NumberRounding.Downward, NumberRounding.ToNearest);
                return (inAmount, SwapAssetIn(poolIn, poolOut, inAmount, tradingFee, rules));
            }

            (XrplAmount In, XrplAmount Out) amounts = Amounts(proposed);
            if (XrplQuality.FromAmounts(amounts.In, amounts.Out, rules) < target)
                return Amounts(ReduceOffer(amounts.In, c));

            return amounts;
        }

        /// <summary>The pre-<c>fixAMMv1_1</c> <c>changeSpotPriceQuality</c>, which throws where the newer one declines.</summary>
        private static (XrplAmount In, XrplAmount Out)? ChangeSpotPriceQualityLegacy(
            XrplAmount poolIn,
            XrplAmount poolOut,
            XrplQuality quality,
            ushort tradingFee,
            LedgerRules rules)
        {
            NumberContext c = rules.Context;
            XrplNumber rate = quality.Rate;
            XrplNumber f = AmmFormulas.FeeMult(tradingFee, c);
            XrplNumber a = f;
            XrplNumber b = XrplNumber.Multiply(poolIn.Value, XrplNumber.Add(1, f, c), c);
            XrplNumber cc = XrplNumber.Subtract(
                XrplNumber.Multiply(poolIn.Value, poolIn.Value, c),
                XrplNumber.Multiply(XrplNumber.Multiply(poolIn.Value, poolOut.Value, c), rate, c),
                c);
            XrplNumber discriminant = XrplNumber.Subtract(
                XrplNumber.Multiply(b, b, c),
                XrplNumber.Multiply(XrplNumber.Multiply(4, a, c), cc, c),
                c);
            if (discriminant < XrplNumber.Zero)
                return null;

            XrplNumber proposed = XrplNumber.Divide(
                XrplNumber.Add(-b, XrplNumber.Root2(discriminant, c), c),
                XrplNumber.Multiply(2, a, c),
                c);
            if (proposed <= XrplNumber.Zero)
                return null;

            XrplNumber constraint = XrplNumber.Subtract(
                XrplNumber.Multiply(poolOut.Value, rate, c),
                XrplNumber.Divide(poolIn.Value, f, c),
                c);
            XrplNumber takerPaysValue = proposed > constraint ? constraint : proposed;
            if (takerPaysValue <= XrplNumber.Zero)
                return null;

            XrplAmount takerPays = StepMath.ToAmount(poolIn.Asset, takerPaysValue, NumberRounding.Upward, c.Rounding);
            (XrplAmount In, XrplAmount Out) amounts = (takerPays, SwapAssetIn(poolIn, poolOut, takerPays, tradingFee, rules));
            XrplQuality offered = XrplQuality.FromAmounts(amounts.In, amounts.Out, rules);
            if (offered < quality && !XrplQuality.WithinRelativeDistance(offered, quality, new XrplNumber(1, -7), rules))
                throw new InvalidOperationException("changeSpotPriceQuality failed");

            return amounts;
        }

        /// <summary>
        /// <c>amount * n</c> as rippled types it: an XRP or MPT amount multiplies exactly as an
        /// integer before it becomes a <c>Number</c>; an issued currency is a <c>Number</c> already.
        /// </summary>
        internal static XrplNumber TypedTimes(XrplAmount amount, long n, NumberContext c)
        {
            if (!amount.IsIntegral)
                return XrplNumber.Multiply(amount.Value, (XrplNumber)n, c);

            long units = checked((amount.IsNegative ? -(long)amount.StMantissa : (long)amount.StMantissa) * n);
            return XrplNumber.Multiply((XrplNumber)units, 1, c);
        }

        /// <summary><c>solveQuadraticEqSmallest</c>: the smaller root, in the numerically stable form.</summary>
        internal static XrplNumber? SolveQuadraticSmallest(XrplNumber a, XrplNumber b, XrplNumber c, NumberContext ctx)
        {
            XrplNumber d = XrplNumber.Subtract(
                XrplNumber.Multiply(b, b, ctx),
                XrplNumber.Multiply(XrplNumber.Multiply(4, a, ctx), c, ctx),
                ctx);
            if (d < XrplNumber.Zero)
                return null;

            XrplNumber twoC = XrplNumber.Multiply(2, c, ctx);
            XrplNumber root = XrplNumber.Root2(d, ctx);
            return b > XrplNumber.Zero
                ? XrplNumber.Divide(twoC, XrplNumber.Subtract(-b, root, ctx), ctx)
                : XrplNumber.Divide(twoC, XrplNumber.Add(-b, root, ctx), ctx);
        }

        /// <summary><c>detail::reduceOffer</c>: 99.99% of the amount, toward zero.</summary>
        private static XrplNumber ReduceOffer(XrplAmount amount, NumberContext c) =>
            XrplNumber.Multiply(amount.Value, ReducedOfferPct, c.WithRounding(NumberRounding.TowardsZero));

        /// <summary>Whether two numbers are within a relative distance: <c>(max - min) / max &lt; dist</c>.</summary>
        internal static bool WithinRelativeDistance(XrplNumber a, XrplNumber b, XrplNumber distance, NumberContext c)
        {
            if (a == b)
                return true;

            XrplNumber min = a < b ? a : b;
            XrplNumber max = a < b ? b : a;
            return XrplNumber.Divide(XrplNumber.Subtract(max, min, c), max, c) < distance;
        }
    }
}
