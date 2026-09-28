using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

namespace Xrpl.Amounts
{
    /// <summary>
    /// rippled's <c>QualityFunction</c>: a strand's average quality as a linear function of its
    /// output, <c>q(out) = m * out + b</c>. An order book offer is a constant; a single-path AMM
    /// offer is not, which is what lets the engine size the output to reach a limit quality.
    /// </summary>
    internal sealed class QualityFunction
    {
        private XrplNumber _m;
        private XrplNumber _b;

        private QualityFunction(XrplNumber m, XrplNumber b, XrplQuality? quality)
        {
            _m = m;
            _b = b;
            Quality = quality;
        }

        /// <summary>The constant quality, while the function is one.</summary>
        internal XrplQuality? Quality { get; private set; }

        internal bool IsConst => Quality.HasValue;

        /// <summary><c>QualityFunction(quality, CLOBLikeTag)</c>.</summary>
        internal static QualityFunction ClobLike(XrplQuality quality, NumberContext c)
        {
            XrplNumber rate = quality.Rate;
            if (rate <= XrplNumber.Zero)
                throw new InvalidOperationException("QualityFunction quality rate is 0.");

            return new QualityFunction(XrplNumber.Zero, XrplNumber.Divide(1, rate, c), quality);
        }

        /// <summary><c>QualityFunction(balances, tfee, AMMTag)</c>.</summary>
        internal static QualityFunction Amm(XrplAmount balanceIn, XrplAmount balanceOut, ushort tradingFee, NumberContext c)
        {
            if (StepMath.IsNotPositive(balanceIn) || StepMath.IsNotPositive(balanceOut))
                throw new InvalidOperationException("QualityFunction amounts are 0.");

            XrplNumber cfee = AmmFormulas.FeeMult(tradingFee, c);
            XrplNumber m = XrplNumber.Divide(-cfee, balanceIn.Value, c);
            XrplNumber b = XrplNumber.Divide(XrplNumber.Multiply(balanceOut.Value, cfee, c), balanceIn.Value, c);
            return new QualityFunction(m, b, null);
        }

        /// <summary><c>combine</c>: this function followed by the next step's.</summary>
        internal void Combine(QualityFunction next, NumberContext c)
        {
            _m = XrplNumber.Add(_m, XrplNumber.Multiply(_b, next._m, c), c);
            _b = XrplNumber.Multiply(_b, next._b, c);
            if (!_m.IsZero)
                Quality = null;
        }

        /// <summary><c>outFromAvgQ</c>: the output that gives <paramref name="quality"/> on average, rounded up.</summary>
        internal XrplNumber? OutFromAvgQ(XrplQuality quality, NumberContext c)
        {
            XrplNumber rate = quality.Rate;
            if (_m.IsZero || rate.IsZero)
                return null;

            NumberContext up = c.WithRounding(NumberRounding.Upward);
            XrplNumber result = XrplNumber.Divide(XrplNumber.Subtract(XrplNumber.Divide(1, rate, up), _b, up), _m, up);
            return result <= XrplNumber.Zero ? null : result;
        }

        /// <summary><c>satisfiesAvgQ</c>: whether <paramref name="output"/> gives at least <paramref name="quality"/> on average.</summary>
        internal bool SatisfiesAvgQ(XrplQuality quality, XrplNumber output, NumberContext c)
        {
            XrplNumber rate = quality.Rate;
            if (rate.IsZero)
                return false;

            return XrplNumber.Add(XrplNumber.Multiply(_m, output, c), _b, c) >= XrplNumber.Divide(1, rate, c);
        }
    }
}
