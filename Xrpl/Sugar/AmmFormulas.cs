using System;
using System.Numerics;

using Xrpl.BinaryCodec.Numbers;

namespace Xrpl.Sugar
{
    /// <summary>What an <c>STAmount</c> holds, which decides how it rounds.</summary>
    internal enum AmountKind
    {
        Xrp,
        Iou,
        Mpt,
    }

    /// <summary>
    /// The <c>STAmount</c> arithmetic rippled's AMM code relies on, in <see cref="XrplNumber"/>:
    /// conversion from a <c>Number</c>, addition, <c>divide</c> and <c>multiply</c>.
    /// </summary>
    internal static class AmountMath
    {
        private const ulong MinIouMantissa = 1_000_000_000_000_000;
        private const ulong TenTo17 = 100_000_000_000_000_000;

        /// <summary>
        /// <c>toSTAmount(asset, n, mode)</c>: XRP is the signed value converted to drops, an MPT the
        /// magnitude converted to a whole unit, an IOU the magnitude rounded to 16 digits; all in
        /// the context's mode.
        /// </summary>
        internal static XrplNumber ToAmount(XrplNumber value, AmountKind kind, NumberContext c) => kind switch
        {
            AmountKind.Xrp => (XrplNumber)value.ToInt64(c),
            AmountKind.Mpt => AssetRounding.ToAmount(value, integral: true, c),
            _ => AssetRounding.ToAmount(value, integral: false, c),
        };

        /// <summary>
        /// <c>STAmount + STAmount</c>: exact for XRP and MPT; for an IOU the <c>Number</c> sum
        /// normalized to 16 digits (<c>IOUAmount</c>), both in the context's mode.
        /// </summary>
        internal static XrplNumber Add(XrplNumber a, XrplNumber b, AmountKind kind, NumberContext c)
        {
            if (b.IsZero)
                return a;
            if (a.IsZero)
                return b;

            XrplNumber sum = XrplNumber.Add(a, b, c);
            return kind == AmountKind.Iou ? AssetRounding.RoundToIouDigits(sum, c.Rounding) : sum;
        }

        /// <summary><c>STAmount - STAmount</c>.</summary>
        internal static XrplNumber Subtract(XrplNumber a, XrplNumber b, AmountKind kind, NumberContext c) =>
            Add(a, -b, kind, c);

        /// <summary>
        /// <c>divide(num, den, issue)</c> into an IOU: both mantissas brought to at least 10^15,
        /// <c>num * 10^17 / den</c> truncated, plus 5, then normalized to 16 digits.
        /// </summary>
        internal static XrplNumber Divide(XrplNumber num, AmountKind numKind, XrplNumber den, AmountKind denKind, NumberContext c)
        {
            if (den.IsZero)
                throw new DivideByZeroException("division by zero");
            if (num.IsZero)
                return XrplNumber.Zero;

            (BigInteger numMantissa, int numOffset) = Mantissa(num, numKind);
            (BigInteger denMantissa, int denOffset) = Mantissa(den, denKind);
            BigInteger quotient = numMantissa * TenTo17 / denMantissa;
            if (quotient > ulong.MaxValue)
                throw new OverflowException("overflow in divide");

            quotient += 5;
            bool negative = num.IsNegative != den.IsNegative;
            XrplNumber raw = XrplNumber.Add(
                XrplNumber.Zero,
                new XrplNumber(negative ? -(long)quotient : (long)quotient, numOffset - denOffset - 17),
                c);
            return AssetRounding.RoundToIouDigits(raw, c.Rounding);
        }

        /// <summary><c>multiply(amount, frac, rm)</c>: the product in the mode, converted to the amount's kind in it.</summary>
        internal static XrplNumber Multiply(XrplNumber amount, AmountKind kind, XrplNumber fraction, NumberContext c, NumberRounding rounding)
        {
            NumberContext directed = c.WithRounding(rounding);
            return ToAmount(XrplNumber.Multiply(amount, fraction, directed), kind, directed);
        }

        /// <summary>The <c>STAmount</c> mantissa and exponent, an integral one scaled to at least 10^15.</summary>
        private static (BigInteger Mantissa, int Offset) Mantissa(XrplNumber value, AmountKind kind)
        {
            BigInteger mantissa = BigInteger.Abs(value.Mantissa);
            int offset = value.Exponent;
            if (kind == AmountKind.Iou)
            {
                // The IOU form: mantissa in [10^15, 10^16).
                while (mantissa >= MinIouMantissa * 10)
                {
                    mantissa /= 10;
                    offset++;
                }
            }
            else
            {
                // An integral amount has an exponent of zero.
                while (offset > 0)
                {
                    mantissa *= 10;
                    offset--;
                }
            }

            while (mantissa < MinIouMantissa)
            {
                mantissa *= 10;
                offset--;
            }

            return (mantissa, offset);
        }
    }

    /// <summary>
    /// rippled's <c>AMMHelpers.cpp</c> under <c>fixAMMv1_3</c>: the XLS-30 equations with the
    /// rounding that keeps <c>sqrt(pool1 * pool2) &gt;= LPTokenBalance</c>.
    /// </summary>
    internal static class AmmFormulas
    {
        private const long FeeScale = 100_000;

        /// <summary><c>getFee</c>: the trading fee as a fraction.</summary>
        internal static XrplNumber Fee(ushort tradingFee, NumberContext c) =>
            XrplNumber.Divide((XrplNumber)(long)tradingFee, (XrplNumber)FeeScale, c);

        /// <summary><c>feeMult</c>: 1 - fee.</summary>
        internal static XrplNumber FeeMult(ushort tradingFee, NumberContext c) =>
            XrplNumber.Subtract(1, Fee(tradingFee, c), c);

        /// <summary><c>feeMultHalf</c>: 1 - fee / 2.</summary>
        internal static XrplNumber FeeMultHalf(ushort tradingFee, NumberContext c) =>
            XrplNumber.Subtract(1, XrplNumber.Divide(Fee(tradingFee, c), 2, c), c);

        /// <summary><c>ammLPTokens</c>: the LP tokens of a new pool, rounded down.</summary>
        internal static XrplNumber InitialTokens(XrplNumber amount1, XrplNumber amount2, NumberContext c)
        {
            NumberContext down = c.WithRounding(NumberRounding.Downward);
            XrplNumber tokens = XrplNumber.Root2(XrplNumber.Multiply(amount1, amount2, down), down);
            return AmountMath.ToAmount(tokens, AmountKind.Iou, down);
        }

        /// <summary>Equation 3, <c>lpTokensOut</c>: LP tokens for a single-asset deposit, rounded down.</summary>
        internal static XrplNumber LpTokensOut(XrplNumber balance, XrplNumber deposit, XrplNumber lpTokenBalance, ushort fee, NumberContext c)
        {
            XrplNumber f1 = FeeMult(fee, c);
            XrplNumber f2 = XrplNumber.Divide(FeeMultHalf(fee, c), f1, c);
            XrplNumber r = XrplNumber.Divide(deposit, balance, c);
            XrplNumber root = XrplNumber.Root2(XrplNumber.Add(XrplNumber.Multiply(f2, f2, c), XrplNumber.Divide(r, f1, c), c), c);
            XrplNumber cc = XrplNumber.Subtract(root, f2, c);
            XrplNumber frac = XrplNumber.Divide(XrplNumber.Subtract(r, cc, c), XrplNumber.Add(1, cc, c), c);
            return AmountMath.Multiply(lpTokenBalance, AmountKind.Iou, frac, c, NumberRounding.Downward);
        }

        /// <summary>Equation 4, <c>ammAssetIn</c>: the asset a single-asset deposit takes for LP tokens, rounded up.</summary>
        internal static XrplNumber AssetIn(XrplNumber balance, AmountKind kind, XrplNumber lpTokenBalance, XrplNumber tokens, ushort fee, NumberContext c)
        {
            XrplNumber f1 = FeeMult(fee, c);
            XrplNumber f2 = XrplNumber.Divide(FeeMultHalf(fee, c), f1, c);
            XrplNumber t1 = XrplNumber.Divide(tokens, lpTokenBalance, c);
            XrplNumber t2 = XrplNumber.Add(1, t1, c);
            XrplNumber d = XrplNumber.Subtract(f2, XrplNumber.Divide(t1, t2, c), c);
            XrplNumber a = XrplNumber.Divide(1, XrplNumber.Multiply(t2, t2, c), c);
            XrplNumber b = XrplNumber.Subtract(
                XrplNumber.Divide(XrplNumber.Multiply(2, d, c), t2, c),
                XrplNumber.Divide(1, f1, c),
                c);
            XrplNumber cc = XrplNumber.Subtract(XrplNumber.Multiply(d, d, c), XrplNumber.Multiply(f2, f2, c), c);
            return AmountMath.Multiply(balance, kind, SolveQuadratic(a, b, cc, c), c, NumberRounding.Upward);
        }

        /// <summary>Equation 7, <c>lpTokensIn</c>: LP tokens a single-asset withdrawal burns, rounded up.</summary>
        internal static XrplNumber LpTokensIn(XrplNumber balance, XrplNumber withdrawal, XrplNumber lpTokenBalance, ushort fee, NumberContext c)
        {
            XrplNumber fr = XrplNumber.Divide(withdrawal, balance, c);
            XrplNumber f1 = Fee(fee, c);
            XrplNumber cc = XrplNumber.Subtract(XrplNumber.Add(XrplNumber.Multiply(fr, f1, c), 2, c), f1, c);
            XrplNumber root = XrplNumber.Root2(
                XrplNumber.Subtract(XrplNumber.Multiply(cc, cc, c), XrplNumber.Multiply(4, fr, c), c),
                c);
            XrplNumber frac = XrplNumber.Divide(XrplNumber.Subtract(cc, root, c), 2, c);
            return AmountMath.Multiply(lpTokenBalance, AmountKind.Iou, frac, c, NumberRounding.Upward);
        }

        /// <summary>Equation 8, <c>ammAssetOut</c>: the asset a single-asset withdrawal pays for LP tokens, rounded down.</summary>
        internal static XrplNumber AssetOut(XrplNumber balance, AmountKind kind, XrplNumber lpTokenBalance, XrplNumber tokens, ushort fee, NumberContext c)
        {
            XrplNumber f = Fee(fee, c);
            XrplNumber t1 = XrplNumber.Divide(tokens, lpTokenBalance, c);
            XrplNumber numerator = XrplNumber.Subtract(
                XrplNumber.Multiply(t1, t1, c),
                XrplNumber.Multiply(t1, XrplNumber.Subtract(2, f, c), c),
                c);
            XrplNumber denominator = XrplNumber.Subtract(XrplNumber.Multiply(t1, f, c), 1, c);
            return AmountMath.Multiply(balance, kind, XrplNumber.Divide(numerator, denominator, c), c, NumberRounding.Downward);
        }

        /// <summary>
        /// <c>adjustLPTokens</c>: LP tokens as the pool's balance can absorb them, rounded down:
        /// <c>(balance + tokens) - balance</c> on a deposit, <c>(tokens - balance) + balance</c> on a withdrawal.
        /// </summary>
        internal static XrplNumber AdjustTokens(XrplNumber lpTokenBalance, XrplNumber tokens, bool isDeposit, NumberContext c)
        {
            NumberContext down = c.WithRounding(NumberRounding.Downward);
            return isDeposit
                ? AmountMath.Subtract(AmountMath.Add(lpTokenBalance, tokens, AmountKind.Iou, down), lpTokenBalance, AmountKind.Iou, down)
                : AmountMath.Add(AmountMath.Subtract(tokens, lpTokenBalance, AmountKind.Iou, down), lpTokenBalance, AmountKind.Iou, down);
        }

        /// <summary><c>getRoundedAsset</c>: a fraction of a pool balance, rounded up on a deposit and down on a withdrawal.</summary>
        internal static XrplNumber RoundedAsset(XrplNumber balance, AmountKind kind, XrplNumber fraction, bool isDeposit, NumberContext c) =>
            AmountMath.Multiply(balance, kind, fraction, c, isDeposit ? NumberRounding.Upward : NumberRounding.Downward);

        /// <summary><c>getRoundedLPTokens</c>: a fraction of the LP token balance, rounded down on a deposit and up on a withdrawal, then adjusted.</summary>
        internal static XrplNumber RoundedTokens(XrplNumber lpTokenBalance, XrplNumber fraction, bool isDeposit, NumberContext c)
        {
            XrplNumber tokens = AmountMath.Multiply(
                lpTokenBalance,
                AmountKind.Iou,
                fraction,
                c,
                isDeposit ? NumberRounding.Downward : NumberRounding.Upward);
            return AdjustTokens(lpTokenBalance, tokens, isDeposit, c);
        }

        /// <summary>
        /// <c>adjustAssetInByTokens</c>: when the asset the tokens cost exceeds the amount, the
        /// tokens are recomputed from a smaller amount; the asset never exceeds the amount.
        /// </summary>
        internal static (XrplNumber Tokens, XrplNumber Asset) AdjustAssetIn(
            XrplNumber balance,
            AmountKind kind,
            XrplNumber amount,
            XrplNumber lpTokenBalance,
            XrplNumber tokens,
            ushort fee,
            NumberContext c)
        {
            XrplNumber asset = AssetIn(balance, kind, lpTokenBalance, tokens, fee, c);
            XrplNumber adjustedTokens = tokens;
            if (asset > amount)
            {
                XrplNumber smaller = AmountMath.Subtract(amount, AmountMath.Subtract(asset, amount, kind, c), kind, c);
                XrplNumber t = LpTokensOut(balance, smaller, lpTokenBalance, fee, c);
                adjustedTokens = AdjustTokens(lpTokenBalance, t, isDeposit: true, c);
                asset = AssetIn(balance, kind, lpTokenBalance, adjustedTokens, fee, c);
            }

            return (adjustedTokens, asset < amount ? asset : amount);
        }

        /// <summary><c>adjustAssetOutByTokens</c>: the withdrawal counterpart of <see cref="AdjustAssetIn"/>.</summary>
        internal static (XrplNumber Tokens, XrplNumber Asset) AdjustAssetOut(
            XrplNumber balance,
            AmountKind kind,
            XrplNumber amount,
            XrplNumber lpTokenBalance,
            XrplNumber tokens,
            ushort fee,
            NumberContext c)
        {
            XrplNumber asset = AssetOut(balance, kind, lpTokenBalance, tokens, fee, c);
            XrplNumber adjustedTokens = tokens;
            if (asset > amount)
            {
                XrplNumber smaller = AmountMath.Subtract(amount, AmountMath.Subtract(asset, amount, kind, c), kind, c);
                XrplNumber t = LpTokensIn(balance, smaller, lpTokenBalance, fee, c);
                adjustedTokens = AdjustTokens(lpTokenBalance, t, isDeposit: false, c);
                asset = AssetOut(balance, kind, lpTokenBalance, adjustedTokens, fee, c);
            }

            return (adjustedTokens, asset < amount ? asset : amount);
        }

        /// <summary>
        /// <c>checkAMMPrecisionLoss</c>: whether the pool's geometric mean still covers the LP token
        /// balance, allowing a relative distance of 10^-11.
        /// </summary>
        internal static bool KeepsInvariant(XrplNumber pool1, XrplNumber pool2, XrplNumber newLpTokenBalance, NumberContext c)
        {
            if (newLpTokenBalance <= XrplNumber.Zero)
                return true;

            XrplNumber mean = XrplNumber.Root2(XrplNumber.Multiply(pool1, pool2, c), c);
            if (mean >= newLpTokenBalance)
                return true;

            // withinRelativeDistance: (max - min) / max < 1e-11, max being the LP token balance here.
            XrplNumber distance = XrplNumber.Divide(XrplNumber.Subtract(newLpTokenBalance, mean, c), newLpTokenBalance, c);
            return distance < new XrplNumber(1, -11);
        }

        /// <summary>Whether two amounts are within a relative distance: <c>(max - min) / max &lt; dist</c>.</summary>
        internal static bool WithinRelativeDistance(XrplNumber a, XrplNumber b, XrplNumber distance, NumberContext c)
        {
            if (a == b)
                return true;

            XrplNumber min = a < b ? a : b;
            XrplNumber max = a < b ? b : a;
            return XrplNumber.Divide(XrplNumber.Subtract(max, min, c), max, c) < distance;
        }

        /// <summary><c>solveQuadraticEq</c>: the larger root, <c>(-b + sqrt(b^2 - 4ac)) / 2a</c>.</summary>
        private static XrplNumber SolveQuadratic(XrplNumber a, XrplNumber b, XrplNumber c, NumberContext ctx)
        {
            XrplNumber discriminant = XrplNumber.Subtract(
                XrplNumber.Multiply(b, b, ctx),
                XrplNumber.Multiply(XrplNumber.Multiply(4, a, ctx), c, ctx),
                ctx);
            return XrplNumber.Divide(
                XrplNumber.Add(-b, XrplNumber.Root2(discriminant, ctx), ctx),
                XrplNumber.Multiply(2, a, ctx),
                ctx);
        }
    }
}
