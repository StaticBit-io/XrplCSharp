using System;
using System.Numerics;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// rippled's <c>STAmount</c> arithmetic on <see cref="XrplAmount"/>, bit for bit: addition,
    /// <c>multiply</c>, <c>divide</c>, and the directed rounding <c>mulRound</c> / <c>divRound</c>
    /// that offer crossing uses, in both their legacy and strict forms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="LedgerRules"/> supplies what the node switches on: the <c>Number</c> scale, and
    /// <c>MPTokensV2</c>, under which an MPT result goes through <c>Number</c> arithmetic. null
    /// means the current rules.
    /// </para>
    /// <para>
    /// The strict forms differ from the legacy ones in two places only. An integral result (XRP,
    /// MPT) keeps every discarded digit when it decides to round up. And the final amount is built
    /// under a fixed rounding mode - toward zero for a product, away from or toward zero as asked
    /// for a quotient - where the legacy forms use the ambient mode, rounding to nearest. So a
    /// legacy <see cref="MulRound"/> asked to round down may round an issued currency to nearest.
    /// </para>
    /// </remarks>
    public static class XrplAmountMath
    {
        private const ulong TenTo14 = 100_000_000_000_000;
        private const ulong TenTo14Minus1 = TenTo14 - 1;
        private const ulong TenTo17 = TenTo14 * 1000;

        /// <summary><c>STAmount + STAmount</c>.</summary>
        /// <exception cref="ArgumentException">The amounts are of different assets.</exception>
        /// <exception cref="OverflowException">The sum is beyond what the asset can hold.</exception>
        public static XrplAmount Add(XrplAmount a, XrplAmount b, LedgerRules rules = null)
        {
            XrplAmount.RequireSameAsset(a, b);
            if (b.IsZero)
                return a;
            if (a.IsZero)
                return b;

            NumberContext c = Context(rules);
            if (a.IsIntegral)
            {
                BigInteger sum = Signed(a) + Signed(b);
                if (sum > long.MaxValue || sum < long.MinValue)
                    throw new OverflowException("amount overflow");

                return XrplAmount.Canonical(a.Asset, a.Kind, sum.Sign < 0, BigInteger.Abs(sum), 0, c.Rounding);
            }

            // IOUAmount + IOUAmount: the Number sum in the ledger's scale, then normalized to 16
            // digits with the sign taken into account.
            XrplNumber total = XrplNumber.Add(a.Value, b.Value, c);
            return Exact(a.Asset, a.Kind, total, c.Rounding);
        }

        /// <summary><c>STAmount - STAmount</c>.</summary>
        /// <exception cref="ArgumentException">The amounts are of different assets.</exception>
        /// <exception cref="OverflowException">The difference is beyond what the asset can hold.</exception>
        public static XrplAmount Subtract(XrplAmount a, XrplAmount b, LedgerRules rules = null) => Add(a, -b, rules);

        /// <summary>
        /// <c>multiply(amount, frac, rm)</c>: the product computed in <paramref name="rounding"/>,
        /// then made an amount of the asset in the same mode (<c>toSTAmount</c>).
        /// </summary>
        public static XrplAmount Multiply(XrplAmount amount, XrplNumber fraction, NumberRounding rounding, LedgerRules rules = null)
        {
            NumberContext directed = Context(rules).WithRounding(rounding);
            XrplNumber product = XrplNumber.Multiply(amount.Value, fraction, directed);
            return Exact(amount.Asset, amount.Kind, AmountMath.ToAmount(product, amount.Kind, directed), rounding);
        }

        /// <summary>
        /// <c>multiply(v1, v2, asset)</c>: the product as an amount of <paramref name="asset"/>,
        /// exact for XRP x XRP and MPT x MPT, otherwise through <c>Number</c>.
        /// </summary>
        /// <exception cref="OverflowException">The product is beyond what the asset can hold.</exception>
        public static XrplAmount Multiply(XrplAmount v1, XrplAmount v2, IssuedCurrency asset, LedgerRules rules = null)
        {
            if (v1.IsZero || v2.IsZero)
                return XrplAmount.Zero(asset);

            AmountKind kind = XrplAmount.KindOf(asset);
            if (IntegralProduct(v1, v2, kind) is { } exact)
                return XrplAmount.FromStParts(asset, kind, (ulong)exact, 0, false, NumberRounding.ToNearest);

            NumberContext c = Context(rules);
            return XrplAmount.FromNumber(asset, XrplNumber.Multiply(v1.Value, v2.Value, c), c.Rounding);
        }

        /// <summary>
        /// <c>divide(num, den, asset)</c>: both mantissas scaled to at least 10^15,
        /// <c>num * 10^17 / den</c> truncated, plus 5, as an amount of <paramref name="asset"/>.
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="den"/> is zero.</exception>
        /// <exception cref="OverflowException">The quotient is beyond what the asset can hold.</exception>
        public static XrplAmount Divide(XrplAmount num, XrplAmount den, IssuedCurrency asset, LedgerRules rules = null) =>
            DivideInto(num, den, asset, XrplAmount.KindOf(asset), Context(rules).Rounding);

        /// <summary>
        /// <c>mulRound</c>: the product as an amount of <paramref name="asset"/>, away from zero when
        /// <paramref name="roundUp"/>, otherwise toward zero - with the legacy rounding described on
        /// the class.
        /// </summary>
        public static XrplAmount MulRound(XrplAmount v1, XrplAmount v2, IssuedCurrency asset, bool roundUp, LedgerRules rules = null) =>
            MulRoundImpl(v1, v2, asset, XrplAmount.KindOf(asset), roundUp, strict: false, rules);

        /// <summary><c>mulRoundStrict</c>: <see cref="MulRound"/> with every discarded digit counted.</summary>
        public static XrplAmount MulRoundStrict(XrplAmount v1, XrplAmount v2, IssuedCurrency asset, bool roundUp, LedgerRules rules = null) =>
            MulRoundImpl(v1, v2, asset, XrplAmount.KindOf(asset), roundUp, strict: true, rules);

        /// <summary>
        /// <c>divRound</c>: the quotient as an amount of <paramref name="asset"/>, away from zero when
        /// <paramref name="roundUp"/>, otherwise toward zero - with the legacy rounding described on
        /// the class.
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="den"/> is zero.</exception>
        public static XrplAmount DivRound(XrplAmount num, XrplAmount den, IssuedCurrency asset, bool roundUp, LedgerRules rules = null) =>
            DivRoundImpl(num, den, asset, XrplAmount.KindOf(asset), roundUp, strict: false, rules);

        /// <summary><c>divRoundStrict</c>: <see cref="DivRound"/> built in the requested direction.</summary>
        /// <exception cref="DivideByZeroException"><paramref name="den"/> is zero.</exception>
        public static XrplAmount DivRoundStrict(XrplAmount num, XrplAmount den, IssuedCurrency asset, bool roundUp, LedgerRules rules = null) =>
            DivRoundImpl(num, den, asset, XrplAmount.KindOf(asset), roundUp, strict: true, rules);

        /// <summary>
        /// <paramref name="a"/> - <paramref name="b"/> computed exactly, then rounded once to what
        /// the asset holds, to nearest with ties to even - as rippled's <c>Number</c> rounds. Two
        /// amounts close in size subtract exactly; far apart, the sixteenth digit is rounded.
        /// </summary>
        internal static XrplAmount ExactDifference(XrplAmount a, XrplAmount b)
        {
            if (b.IsZero)
                return a;
            if (a.IsZero)
                return -b;

            int exponent = Math.Min(a.StExponent, b.StExponent);
            BigInteger difference = Signed(a) * BigInteger.Pow(10, a.StExponent - exponent)
                                    - Signed(b) * BigInteger.Pow(10, b.StExponent - exponent);
            return XrplAmount.Canonical(a.Asset, a.Kind, difference.Sign < 0, BigInteger.Abs(difference), exponent, NumberRounding.ToNearest);
        }

        /// <summary><c>divide</c> into an amount of the given kind, built in <paramref name="ambient"/>.</summary>
        internal static XrplAmount DivideInto(XrplAmount num, XrplAmount den, IssuedCurrency asset, AmountKind kind, NumberRounding ambient)
        {
            if (den.IsZero)
                throw new DivideByZeroException("division by zero");
            if (num.IsZero)
                return kind == AmountKind.Iou && asset == null ? XrplAmount.Rate(0, 0) : XrplAmount.Zero(asset);

            (ulong numVal, int numOffset) = Scaled(num);
            (ulong denVal, int denOffset) = Scaled(den);
            ulong quotient = unchecked((ulong)MulDiv(numVal, TenTo17, denVal, 0) + 5);
            return XrplAmount.FromStParts(asset, kind, quotient, numOffset - denOffset - 17, num.IsNegative != den.IsNegative, ambient);
        }

        internal static XrplAmount MulRoundImpl(
            XrplAmount v1,
            XrplAmount v2,
            IssuedCurrency asset,
            AmountKind kind,
            bool roundUp,
            bool strict,
            LedgerRules rules)
        {
            if (v1.IsZero || v2.IsZero)
                return XrplAmount.Zero(asset);

            if (IntegralProduct(v1, v2, kind) is { } exact)
                return XrplAmount.FromStParts(asset, kind, (ulong)exact, 0, false, NumberRounding.ToNearest);

            bool resultNegative = v1.IsNegative != v2.IsNegative;
            NumberContext c = Context(rules);

            if (kind == AmountKind.Mpt && (rules ?? new LedgerRules()).MPTokensV2)
            {
                NumberRounding mode = RoundMode(resultNegative, roundUp);
                XrplNumber product = XrplNumber.Multiply(v1.Value, v2.Value, c.WithRounding(mode));
                return RoundNumberResult(asset, resultNegative, roundUp, product, mode);
            }

            (ulong value1, int offset1) = Scaled(v1);
            (ulong value2, int offset2) = Scaled(v2);

            // The mantissas are each in [10^15, 10^16): their product over 10^14 keeps the
            // precision in [10^16, 10^18]. Rounding up adds 10^14 - 1 first; down truncates.
            ulong amount = (ulong)MulDiv(value1, value2, TenTo14, resultNegative != roundUp ? TenTo14Minus1 : 0);
            int offset = offset1 + offset2 + 14;
            if (resultNegative != roundUp)
            {
                if (strict)
                    CanonicalizeRoundStrict(kind != AmountKind.Iou, ref amount, ref offset, roundUp);
                else
                    CanonicalizeRound(kind != AmountKind.Iou, ref amount, ref offset);
            }

            // The strict form builds the result toward zero; the legacy one in the ambient mode.
            NumberRounding build = strict ? NumberRounding.TowardsZero : c.Rounding;
            XrplAmount result = XrplAmount.FromStParts(asset, kind, amount, offset, resultNegative, build);
            return SmallestIfLost(result, asset, kind, roundUp, resultNegative);
        }

        internal static XrplAmount DivRoundImpl(
            XrplAmount num,
            XrplAmount den,
            IssuedCurrency asset,
            AmountKind kind,
            bool roundUp,
            bool strict,
            LedgerRules rules)
        {
            if (den.IsZero)
                throw new DivideByZeroException("division by zero");
            if (num.IsZero)
                return XrplAmount.Zero(asset);

            bool resultNegative = num.IsNegative != den.IsNegative;
            NumberContext c = Context(rules);

            if (kind == AmountKind.Mpt && (rules ?? new LedgerRules()).MPTokensV2)
            {
                NumberRounding mode = RoundMode(resultNegative, roundUp);
                XrplNumber quotient = XrplNumber.Divide(num.Value, den.Value, c.WithRounding(mode));
                return RoundNumberResult(asset, resultNegative, roundUp, quotient, mode);
            }

            (ulong numVal, int numOffset) = Scaled(num);
            (ulong denVal, int denOffset) = Scaled(den);

            // num * 10^17 / den keeps the quotient in [10^15, 10^17]; rounding up adds den - 1.
            ulong amount = (ulong)MulDiv(numVal, TenTo17, denVal, resultNegative != roundUp ? denVal - 1 : 0);
            int offset = numOffset - denOffset - 17;

            // The legacy canonicalize, in the strict form too.
            if (resultNegative != roundUp)
                CanonicalizeRound(kind != AmountKind.Iou, ref amount, ref offset);

            NumberRounding build = strict ? RoundMode(resultNegative, roundUp) : c.Rounding;
            XrplAmount result = XrplAmount.FromStParts(asset, kind, amount, offset, resultNegative, build);
            return SmallestIfLost(result, asset, kind, roundUp, resultNegative);
        }

        /// <summary><c>canonicalizeRound</c>, the legacy one: it keeps only the last discarded digit.</summary>
        internal static void CanonicalizeRound(bool integral, ref ulong value, ref int offset)
        {
            if (integral)
            {
                if (offset < 0)
                {
                    int loops = 0;
                    while (offset < -1)
                    {
                        value /= 10;
                        ++offset;
                        ++loops;
                    }

                    value += loops >= 2 ? 9UL : 10UL;
                    value /= 10;
                    ++offset;
                }
            }
            else if (value > XrplAmount.MaxIouMantissa)
            {
                while (value > 10 * XrplAmount.MaxIouMantissa)
                {
                    value /= 10;
                    ++offset;
                }

                value += 9;
                value /= 10;
                ++offset;
            }
        }

        /// <summary>
        /// <c>canonicalizeRoundStrict</c>: for an integral amount every discarded digit counts; an
        /// issued currency is handled as in the legacy form.
        /// </summary>
        internal static void CanonicalizeRoundStrict(bool integral, ref ulong value, ref int offset, bool roundUp)
        {
            if (integral)
            {
                if (offset < 0)
                {
                    bool hadRemainder = false;
                    while (offset < -1)
                    {
                        ulong newValue = value / 10;
                        hadRemainder |= value != newValue * 10;
                        value = newValue;
                        ++offset;
                    }

                    value += hadRemainder && roundUp ? 10UL : 9UL;
                    value /= 10;
                    ++offset;
                }
            }
            else if (value > XrplAmount.MaxIouMantissa)
            {
                while (value > 10 * XrplAmount.MaxIouMantissa)
                {
                    value /= 10;
                    ++offset;
                }

                value += 9;
                value /= 10;
                ++offset;
            }
        }

        /// <summary>A value as an amount, rounded to the asset with the sign taken into account.</summary>
        private static XrplAmount Exact(IssuedCurrency asset, AmountKind kind, XrplNumber value, NumberRounding rounding) =>
            value.IsZero
                ? XrplAmount.Zero(asset)
                : XrplAmount.Canonical(asset, kind, value.IsNegative, BigInteger.Abs(value.Mantissa), value.Exponent, rounding);

        /// <summary><c>roundMode</c>: away from zero is up for a positive result and down for a negative one.</summary>
        internal static NumberRounding RoundMode(bool resultNegative, bool roundUp) =>
            roundUp ^ resultNegative ? NumberRounding.Upward : NumberRounding.Downward;

        /// <summary>The ledger's arithmetic under <paramref name="rules"/>, in its ambient mode (to nearest).</summary>
        internal static NumberContext Context(LedgerRules rules) => (rules ?? new LedgerRules()).Context;

        /// <summary>
        /// <c>muldivRound</c>: <c>(a * b + rounding) / c</c> in 128 bits.
        /// </summary>
        /// <exception cref="OverflowException">The quotient does not fit 64 bits.</exception>
        internal static BigInteger MulDiv(ulong a, ulong b, ulong c, ulong rounding)
        {
            BigInteger result = ((BigInteger)a * b + rounding) / c;
            if (result > ulong.MaxValue)
                throw new OverflowException($"overflow: (({a} * {b}) + {rounding}) / {c}");

            return result;
        }

        /// <summary>The mantissa and exponent, an integral amount scaled up to at least 10^15.</summary>
        internal static (ulong Value, int Offset) Scaled(XrplAmount amount)
        {
            ulong value = amount.StMantissa;
            int offset = amount.StExponent;
            if (amount.IsIntegral)
            {
                while (value < XrplAmount.MinIouMantissa)
                {
                    value *= 10;
                    --offset;
                }
            }

            return (value, offset);
        }

        private static XrplAmount RoundNumberResult(IssuedCurrency asset, bool resultNegative, bool roundUp, XrplNumber number, NumberRounding mode)
        {
            XrplAmount result = XrplAmount.FromNumber(asset, number, mode);
            return SmallestIfLost(result, asset, XrplAmount.KindOf(asset), roundUp, resultNegative);
        }

        /// <summary>A positive result rounded up never comes out zero: it is the smallest amount instead.</summary>
        private static XrplAmount SmallestIfLost(XrplAmount result, IssuedCurrency asset, AmountKind kind, bool roundUp, bool resultNegative)
        {
            if (!roundUp || resultNegative || !result.IsZero)
                return result;

            return kind == AmountKind.Iou
                ? XrplAmount.FromStParts(asset, kind, XrplAmount.MinIouMantissa, XrplAmount.MinIouExponent, false, NumberRounding.ToNearest)
                : XrplAmount.FromStParts(asset, kind, 1, 0, false, NumberRounding.ToNearest);
        }

        /// <summary>
        /// The exact product rippled computes for XRP x XRP into XRP and MPT x MPT into MPT, with
        /// its overflow limits; null for every other combination.
        /// </summary>
        private static BigInteger? IntegralProduct(XrplAmount v1, XrplAmount v2, AmountKind kind)
        {
            bool bothXrp = v1.Kind == AmountKind.Xrp && v2.Kind == AmountKind.Xrp && kind == AmountKind.Xrp;
            bool bothMpt = v1.Kind == AmountKind.Mpt && v2.Kind == AmountKind.Mpt && kind == AmountKind.Mpt;
            if (!bothXrp && !bothMpt)
                return null;

            // getSNValue / getMPTValue are signed; the product takes them as unsigned, as rippled does.
            ulong a = unchecked((ulong)(long)Signed(v1));
            ulong b = unchecked((ulong)(long)Signed(v2));
            ulong minV = Math.Min(a, b);
            ulong maxV = Math.Max(a, b);
            if (bothXrp && (minV > 3_000_000_000UL || (maxV >> 32) * minV > 2_095_475_792UL))
                throw new OverflowException("Native value overflow");
            if (bothMpt && (minV > 3_037_000_499UL || (maxV >> 32) * minV > 2_147_483_648UL))
                throw new OverflowException("MPT value overflow");

            return unchecked(minV * maxV);
        }

        private static BigInteger Signed(XrplAmount amount) =>
            amount.IsNegative ? -(BigInteger)amount.StMantissa : amount.StMantissa;
    }
}
