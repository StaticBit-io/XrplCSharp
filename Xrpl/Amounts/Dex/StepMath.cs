using System;
using System.Collections.Generic;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// The arithmetic of the payment engine's typed amounts (<c>XRPAmount</c>, <c>IOUAmount</c>,
    /// <c>MPTAmount</c>) on <see cref="XrplAmount"/>: integral amounts are plain <c>int64</c>
    /// values, an issued currency goes through <c>Number</c> and back to 16 digits.
    /// </summary>
    internal static class StepMath
    {
        /// <summary><c>TAmount + TAmount</c>.</summary>
        internal static XrplAmount Add(XrplAmount a, XrplAmount b, LedgerRules rules)
        {
            if (!a.IsIntegral)
                return XrplAmountMath.Add(a, b, rules);

            XrplAmount.RequireSameAsset(a, b);
            long sum = checked(Signed(a) + Signed(b));
            return XrplAmount.FromUnits(a.Asset, a.Kind, sum);
        }

        /// <summary><c>TAmount - TAmount</c>.</summary>
        internal static XrplAmount Subtract(XrplAmount a, XrplAmount b, LedgerRules rules) => Add(a, -b, rules);

        /// <summary>
        /// <c>std::accumulate</c> over a <c>flat_multiset</c>: the amounts added in ascending order,
        /// starting from the smallest. An issued-currency sum rounds at every step, so the order
        /// decides the low digits.
        /// </summary>
        internal static XrplAmount Sum(List<XrplAmount> amounts, IssuedCurrency asset, LedgerRules rules)
        {
            if (amounts.Count == 0)
                return XrplAmount.Zero(asset);

            List<XrplAmount> sorted = new List<XrplAmount>(amounts);
            sorted.Sort((x, y) => x.Value.CompareTo(y.Value));
            XrplAmount total = sorted[0];
            for (int i = 1; i < sorted.Count; i++)
                total = Add(total, sorted[i], rules);

            return total;
        }

        internal static XrplAmount Min(XrplAmount a, XrplAmount b) => b < a ? b : a;

        /// <summary>
        /// The engine's typed form of an amount: an issued currency without its issuer, as rippled's
        /// <c>IOUAmount</c> carries none, so amounts compare along a strand whoever issued them.
        /// </summary>
        internal static XrplAmount Typed(XrplAmount amount) =>
            amount.Kind == AmountKind.Iou ? amount.WithAsset(TypedIou(amount.Asset.Currency)) : amount;

        /// <summary>
        /// The asset of <see cref="Typed"/> for a currency. The issuer is a placeholder no account
        /// can have: an asset without one would read as XRP.
        /// </summary>
        internal static IssuedCurrency TypedIou(string currency) => new IssuedCurrency { Currency = currency, Issuer = TypedIssuer };

        internal const string TypedIssuer = "(any issuer)";

        /// <summary><c>signum() &lt;= 0</c>.</summary>
        internal static bool IsNotPositive(XrplAmount amount) => amount.IsZero || amount.IsNegative;

        /// <summary><c>signum() &gt; 0</c>.</summary>
        internal static bool IsPositive(XrplAmount amount) => !amount.IsZero && !amount.IsNegative;

        /// <summary>
        /// <c>toAmount&lt;T&gt;(asset, n, mode)</c>: an XRP or MPT amount is rounded in
        /// <paramref name="mode"/>; an issued currency ignores it and rounds in the ambient mode.
        /// </summary>
        internal static XrplAmount ToAmount(IssuedCurrency asset, XrplNumber value, NumberRounding mode, NumberRounding ambient)
        {
            AmountKind kind = XrplAmount.KindOf(asset);
            if (kind == AmountKind.Iou)
                return XrplAmount.FromNumber(asset, value, ambient);

            if (value.IsZero)
                return XrplAmount.Zero(asset);

            long units = value.ToInt64(NumberContext.Default.WithRounding(mode));
            return XrplAmount.FromUnits(asset, kind, units);
        }

        /// <summary><c>toMaxAmount&lt;T&gt;</c>: the largest amount of the asset's type.</summary>
        internal static XrplAmount MaxAmount(IssuedCurrency asset)
        {
            AmountKind kind = XrplAmount.KindOf(asset);
            return kind switch
            {
                AmountKind.Xrp => XrplAmount.FromUnits(asset, kind, (long)XrplAmount.MaxDrops),
                AmountKind.Mpt => XrplAmount.FromUnits(asset, kind, (long)XrplAmount.MaxMptAmount),
                _ => XrplAmount.Canonical(asset, kind, false, XrplAmount.MaxIouMantissa, XrplAmount.MaxIouExponent, NumberRounding.ToNearest),
            };
        }

        /// <summary>A transfer rate as the unitless amount rippled multiplies by (<c>detail::asAmount(Rate)</c>).</summary>
        internal static XrplAmount RateAmount(uint rate) => XrplAmount.Rate(rate, -9);

        private static long Signed(XrplAmount amount) =>
            amount.IsNegative ? -(long)amount.StMantissa : (long)amount.StMantissa;

        /// <summary>Throws the node's <c>FlowException</c> equivalent.</summary>
        internal static Exception Internal(string message) => new InvalidOperationException(message);
    }
}
