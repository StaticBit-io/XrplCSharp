using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// rippled's <c>AMMContext</c>: one per transaction, it counts the iterations in which an AMM
    /// offer was consumed and whether the engine runs more than one strand.
    /// </summary>
    internal sealed class AmmFlowContext
    {
        internal const int MaxIterations = 30;

        private bool _ammUsed;

        internal AmmFlowContext(string account)
        {
            Account = account;
        }

        /// <summary>The transaction's account, whose auction-slot discount applies.</summary>
        internal string Account { get; }

        internal bool MultiPath { get; set; }

        internal int CurrentIterations { get; private set; }

        internal bool MaxIterationsReached => CurrentIterations >= MaxIterations;

        internal void SetAmmUsed() => _ammUsed = true;

        /// <summary>Called once the best strand of an iteration is applied.</summary>
        internal void Update()
        {
            if (_ammUsed)
                CurrentIterations++;
            _ammUsed = false;
        }

        /// <summary>Called before each strand is tried.</summary>
        internal void Clear() => _ammUsed = false;
    }

    /// <summary>
    /// rippled's <c>AMMLiquidity</c>: the pool on one book, turned into a synthetic offer sized
    /// against the order book's best quality each time the book step runs.
    /// </summary>
    internal sealed class AmmBookLiquidity
    {
        private static readonly XrplNumber InitialFibSeqPct = new XrplNumber(25, -5);

        private static readonly uint[] Fibonacci =
        {
            1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233, 377, 610, 987,
            1597, 2584, 4181, 6765, 10946, 17711, 28657, 46368, 75025, 121393,
            196418, 317811, 514229, 832040, 1346269,
        };

        internal AmmBookLiquidity(DexView view, DexAmmPool pool, IssuedCurrency @in, IssuedCurrency @out, AmmFlowContext context)
        {
            Pool = pool;
            AssetIn = @in;
            AssetOut = @out;
            Context = context;
            TradingFee = pool.FeeFor(context.Account, view.ParentCloseTime);
            InitialBalances = FetchBalances(view);
        }

        internal DexAmmPool Pool { get; }

        internal IssuedCurrency AssetIn { get; }

        internal IssuedCurrency AssetOut { get; }

        internal AmmFlowContext Context { get; }

        internal ushort TradingFee { get; }

        internal (XrplAmount In, XrplAmount Out) InitialBalances { get; }

        internal bool MultiPath => Context.MultiPath;

        internal (XrplAmount In, XrplAmount Out) FetchBalances(DexView view)
        {
            XrplAmount @in = view.PoolHolds(Pool, AssetIn);
            XrplAmount @out = view.PoolHolds(Pool, AssetOut);
            if (@in.IsNegative || @out.IsNegative)
                throw new InvalidOperationException("AMMLiquidity: invalid balances");

            return (@in, @out);
        }

        /// <summary>
        /// <c>getOffer</c>: the pool's offer against an order book whose best quality is
        /// <paramref name="clobQuality"/>; null when the pool cannot beat it.
        /// </summary>
        internal AmmBookOffer GetOffer(DexView view, XrplQuality? clobQuality)
        {
            if (Context.MaxIterationsReached)
                return null;

            (XrplAmount In, XrplAmount Out) balances = FetchBalances(view);
            if (balances.In.IsZero || balances.Out.IsZero)
                return null;

            LedgerRules rules = view.Rules;
            XrplQuality spotPrice = XrplQuality.FromAmounts(balances.In, balances.Out, rules);
            if (clobQuality is { } clob &&
                (spotPrice <= clob || XrplQuality.WithinRelativeDistance(spotPrice, clob, new XrplNumber(1, -7), rules)))
            {
                return null;
            }

            AmmBookOffer offer;
            try
            {
                offer = Generate(balances, clobQuality, rules);
            }
            catch (Exception exception) when (exception is OverflowException or InvalidOperationException or DivideByZeroException)
            {
                return null;
            }

            if (offer != null && StepMath.IsPositive(offer.In) && StepMath.IsPositive(offer.Out))
                return offer;

            return null;
        }

        private AmmBookOffer Generate((XrplAmount In, XrplAmount Out) balances, XrplQuality? clobQuality, LedgerRules rules)
        {
            if (Context.MultiPath)
            {
                (XrplAmount In, XrplAmount Out) amounts = FibonacciOffer(balances, rules);
                XrplQuality quality = XrplQuality.FromAmounts(amounts.In, amounts.Out, rules);
                if (clobQuality is { } clob && quality < clob)
                    return null;

                return new AmmBookOffer(this, amounts, balances, quality);
            }

            if (clobQuality == null)
                return MaxOffer(balances, rules);

            if (AmmSwap.ChangeSpotPriceQuality(balances.In, balances.Out, clobQuality.Value, TradingFee, rules) is { } changed)
                return new AmmBookOffer(this, changed, balances, XrplQuality.FromAmounts(changed.In, changed.Out, rules));

            if (rules.FixAMMv1_2)
            {
                AmmBookOffer max = MaxOffer(balances, rules);
                if (max != null && XrplQuality.FromAmounts(max.In, max.Out, rules) > clobQuality.Value)
                    return max;
            }

            return null;
        }

        /// <summary><c>generateFibSeqOffer</c>: a fixed slice of the initial pool, scaled up the Fibonacci sequence each iteration.</summary>
        private (XrplAmount In, XrplAmount Out) FibonacciOffer((XrplAmount In, XrplAmount Out) balances, LedgerRules rules)
        {
            NumberContext c = rules.Context;
            XrplAmount @in = StepMath.ToAmount(
                AssetIn,
                XrplNumber.Multiply(InitialFibSeqPct, InitialBalances.In.Value, c),
                NumberRounding.Upward,
                c.Rounding);
            XrplAmount @out = AmmSwap.SwapAssetIn(InitialBalances.In, InitialBalances.Out, @in, TradingFee, rules);
            if (Context.CurrentIterations == 0)
                return (@in, @out);

            @out = StepMath.ToAmount(
                AssetOut,
                AmmSwap.TypedTimes(@out, Fibonacci[Context.CurrentIterations - 1], c),
                NumberRounding.Downward,
                c.Rounding);
            if (@out >= balances.Out)
                throw new OverflowException("AMMLiquidity: generateFibSeqOffer exceeds the balance");

            return (AmmSwap.SwapAssetOut(balances.In, balances.Out, @out, TradingFee, rules), @out);
        }

        /// <summary><c>maxOffer</c>: 99% of the pool's output, at the spot price quality.</summary>
        private AmmBookOffer MaxOffer((XrplAmount In, XrplAmount Out) balances, LedgerRules rules)
        {
            NumberContext c = rules.Context;
            XrplAmount @out = StepMath.ToAmount(
                AssetOut,
                XrplNumber.Multiply(balances.Out.Value, new XrplNumber(99, -2), c),
                NumberRounding.Downward,
                c.Rounding);
            if (StepMath.IsNotPositive(@out) || @out >= balances.Out)
                return null;

            return new AmmBookOffer(
                this,
                (AmmSwap.SwapAssetOut(balances.In, balances.Out, @out, TradingFee, rules), @out),
                balances,
                XrplQuality.FromAmounts(balances.In, balances.Out, rules));
        }
    }

    /// <summary>rippled's <c>AMMOffer</c>: the pool's synthetic offer for one pass of a book step.</summary>
    internal sealed class AmmBookOffer : FlowOffer
    {
        private readonly AmmBookLiquidity _liquidity;
        private readonly (XrplAmount In, XrplAmount Out) _balances;
        private readonly XrplQuality _quality;
        private bool _consumed;

        internal AmmBookOffer(
            AmmBookLiquidity liquidity,
            (XrplAmount In, XrplAmount Out) amounts,
            (XrplAmount In, XrplAmount Out) balances,
            XrplQuality quality)
        {
            _liquidity = liquidity;
            In = amounts.In;
            Out = amounts.Out;
            _balances = balances;
            _quality = quality;
        }

        internal override XrplQuality Quality => _quality;

        internal override string Owner => _liquidity.Pool.Account;

        internal override string Key => null;

        internal override bool IsAmm => true;

        internal override bool IsFunded => true;

        internal override bool FullyConsumed => _consumed;

        internal override (uint In, uint Out) AdjustRates(uint inRate, uint outRate) => (inRate, OfferCrossing.QualityOne);

        internal override (XrplAmount In, XrplAmount Out) LimitOut((XrplAmount In, XrplAmount Out) amount, XrplAmount limit, bool roundUp, LedgerRules rules)
        {
            if (_liquidity.MultiPath)
                return _quality.CeilOutStrict(amount.In, amount.Out, limit, roundUp, rules);

            return (AmmSwap.SwapAssetOut(_balances.In, _balances.Out, limit, _liquidity.TradingFee, rules), limit);
        }

        internal override (XrplAmount In, XrplAmount Out) LimitIn((XrplAmount In, XrplAmount Out) amount, XrplAmount limit, bool roundUp, LedgerRules rules)
        {
            if (_liquidity.MultiPath)
            {
                return (rules ?? new LedgerRules()).CurrentFixReducedOffersV2
                    ? _quality.CeilInStrict(amount.In, amount.Out, limit, roundUp, rules)
                    : _quality.CeilIn(amount.In, amount.Out, limit, rules);
            }

            return (limit, AmmSwap.SwapAssetIn(_balances.In, _balances.Out, limit, _liquidity.TradingFee, rules));
        }

        internal override void Consume(DexView view, XrplAmount consumedIn, XrplAmount consumedOut)
        {
            if (consumedIn > In || consumedOut > Out)
                throw new InvalidOperationException("Invalid consumed AMM offer.");

            _consumed = true;
            _liquidity.Context.SetAmmUsed();
        }

        /// <summary><c>checkInvariant</c>: the pool's product does not shrink, give or take 10^-7.</summary>
        internal override bool CheckInvariant(XrplAmount consumedIn, XrplAmount consumedOut, LedgerRules rules)
        {
            if (consumedIn > In || consumedOut > Out)
                return false;

            NumberContext c = (rules ?? new LedgerRules()).Context;
            XrplNumber product = XrplNumber.Multiply(_balances.In.Value, _balances.Out.Value, c);
            XrplAmount newIn = StepMath.Add(_balances.In, consumedIn, rules);
            XrplAmount newOut = StepMath.Subtract(_balances.Out, consumedOut, rules);
            XrplNumber newProduct = XrplNumber.Multiply(newIn.Value, newOut.Value, c);
            return newProduct >= product || AmmSwap.WithinRelativeDistance(product, newProduct, new XrplNumber(1, -7), c);
        }

        /// <summary><c>getQualityFunc</c>: constant on several strands, the pool's curve on one.</summary>
        internal QualityFunction QualityFunction(LedgerRules rules)
        {
            NumberContext c = (rules ?? new LedgerRules()).Context;
            return _liquidity.MultiPath
                ? Amounts.QualityFunction.ClobLike(_quality, c)
                : Amounts.QualityFunction.Amm(_balances.In, _balances.Out, _liquidity.TradingFee, c);
        }
    }
}
