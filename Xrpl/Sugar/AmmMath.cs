using System;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What a deposit into an AMM pool will be worth in LP tokens, and what a withdrawal will cost -
    /// computed the way rippled computes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The formulas are equations 3 and 7 from rippled's <c>AMMHelpers.cpp</c>, not the ones that
    /// circulate as the "AMM single-sided deposit formula". The circulating one,
    /// <c>T·(√(1 + b·(1 − f/2)/B) − 1)</c>, is close and always wrong in the same direction: a
    /// deposit the size of the pool at a 1% fee gives <c>0.41244·T</c> against rippled's
    /// <c>0.41213·T</c>, an error of 0.08%. <see cref="LPTokensForSingleAssetDeposit"/> reproduces
    /// the node's figure.
    /// </para>
    /// <para>
    /// Two things decide whether an estimate matches what the node actually credits, and neither is
    /// in the formulas:
    /// </para>
    /// <list type="number">
    /// <item><description><b>Whose fee.</b> The holder of the pool's auction slot trades at
    /// <c>DiscountedFee</c> - a tenth of the pool's trading fee - and the node computes their
    /// deposits and withdrawals at that rate too. Estimating at the pool's fee is wrong for them;
    /// see <see cref="DiscountedTradingFee"/>.</description></item>
    /// <item><description><b>How fresh the pool state is.</b> <c>amm_info</c> has to be read
    /// immediately before the calculation. Run against a snapshot taken when a screen opened, the
    /// drift looks exactly like an error in the arithmetic.</description></item>
    /// </list>
    /// <para>
    /// What comes back is a bound rather than the exact credit, and the direction is known. Under
    /// <c>fixAMMv1_3</c> rippled rounds the final multiplication against the caller in both
    /// directions - <c>lpTokensOut</c> downward ("minimize tokens out"), <c>lpTokensIn</c> upward
    /// ("maximize tokens in") - so a deposit is credited this much or a shade less, and a
    /// withdrawal costs this much or a shade more. The difference lands in the last of
    /// <c>STAmount</c>'s 15 significant digits, which is below what differencing two reported LP
    /// token balances can resolve.
    /// </para>
    /// <para>
    /// Everything is computed in <see cref="decimal"/> rather than <see cref="double"/>: 28
    /// significant digits against 15. That is also why the square root here is Newton's method -
    /// <see cref="Math.Sqrt"/> would throw away the precision the rest of the calculation keeps.
    /// </para>
    /// </remarks>
    public static class AmmMath
    {
        /// <summary>
        /// What a <c>TradingFee</c> of 1 is worth as a fraction: 1/100 000, so 1000 is one per cent.
        /// </summary>
        /// <remarks>
        /// rippled's <c>kAuctionSlotFeeScaleFactor</c>. The field is in units of 1/10 of a basis
        /// point, which is easy to be out by a factor of ten on.
        /// </remarks>
        public const uint TradingFeeScale = 100_000;

        /// <summary>
        /// How much cheaper the auction slot holder's fee is than the pool's.
        /// </summary>
        /// <remarks>rippled's <c>kAuctionSlotDiscountedFeeFraction</c>.</remarks>
        public const uint AuctionSlotFeeDiscount = 10;

        /// <summary>
        /// The largest <c>TradingFee</c> a pool can have: 1000, one per cent.
        /// </summary>
        /// <remarks>
        /// rippled's <c>kTradingFeeThreshold</c>, and the reason a fee is checked against it here.
        /// The field is in units of 1/10 of a basis point, so a caller who reaches for basis points
        /// or for whole per cent is out by a factor of ten or a hundred - and the arithmetic below
        /// would carry on and return a plausible number rather than say so.
        /// </remarks>
        public const uint TradingFeeThreshold = 1000;

        /// <summary>
        /// The trading fee as a fraction of 1.
        /// </summary>
        /// <param name="tradingFee">The pool's <c>TradingFee</c>. rippled caps it at 1000 - one per cent - in <c>kTradingFeeThreshold</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tradingFee"/> exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal TradingFeeFraction(uint tradingFee)
        {
            RequireValidTradingFee(tradingFee);
            return (decimal)tradingFee / TradingFeeScale;
        }

        /// <summary>
        /// The fee the auction slot holder trades at.
        /// </summary>
        /// <remarks>
        /// Use this in place of the pool's fee when the account holding the slot is the one
        /// depositing or withdrawing - the node does, and an estimate at the pool's fee will not
        /// match what it credits.
        /// </remarks>
        /// <param name="tradingFee">The pool's <c>TradingFee</c>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tradingFee"/> exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static uint DiscountedTradingFee(uint tradingFee)
        {
            RequireValidTradingFee(tradingFee);
            return tradingFee / AuctionSlotFeeDiscount;
        }

        /// <summary>
        /// LP tokens credited for depositing one asset only.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Equation 3: with <c>f1 = 1 − fee</c>, <c>f2 = (1 − fee/2)/f1</c> and <c>r = b/B</c>,
        /// </para>
        /// <code>
        /// c = √(f2² + r/f1) − f2
        /// t = T · (r − c) / (1 + c)
        /// </code>
        /// <para>
        /// The node rounds the last multiplication down, so it credits this or a shade less.
        /// </para>
        /// <para>
        /// The plus under the root is deliberate and is what rippled computes. The comment above
        /// that equation in <c>AMMHelpers.cpp</c> writes it as <c>√(f2² − b/(B·f1))</c>, but the
        /// code uses <c>+</c>, and so does the derivation of equation 4 immediately below it. With
        /// a minus the radicand goes negative for ordinary inputs, which settles it.
        /// </para>
        /// </remarks>
        /// <param name="poolBalance">The pool's balance of the asset being deposited, before the deposit.</param>
        /// <param name="deposit">How much of it is being deposited.</param>
        /// <param name="lpTokenBalance">The pool's LP token balance, before the deposit.</param>
        /// <param name="tradingFee">The fee this depositor trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the deposit is negative, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal LPTokensForSingleAssetDeposit(
            decimal poolBalance,
            decimal deposit,
            decimal lpTokenBalance,
            uint tradingFee)
        {
            RequirePositive(poolBalance, nameof(poolBalance));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(deposit, nameof(deposit));
            RequireValidTradingFee(tradingFee);

            if (deposit == 0m)
            {
                return 0m;
            }

            decimal fee = TradingFeeFraction(tradingFee);
            decimal f1 = 1m - fee;
            decimal f2 = (1m - fee / 2m) / f1;
            decimal r = deposit / poolBalance;
            decimal c = Sqrt(f2 * f2 + r / f1) - f2;

            return lpTokenBalance * (r - c) / (1m + c);
        }

        /// <summary>
        /// LP tokens spent to withdraw one asset only.
        /// </summary>
        /// <remarks>
        /// Equation 7: with <c>fr = b/B</c> and <c>c = fr·fee + 2 − fee</c>,
        /// <code>
        /// t = T · (c − √(c² − 4·fr)) / 2
        /// </code>
        /// Note that this one uses the fee itself where <see cref="LPTokensForSingleAssetDeposit"/>
        /// uses <c>1 − fee</c>; rippled's <c>lpTokensIn</c> calls <c>getFee</c> rather than
        /// <c>feeMult</c>, and the difference is easy to lose when transcribing.
        /// The node rounds the last multiplication up here rather than down - both directions go
        /// against the caller - so a withdrawal costs this or a shade more.
        /// </remarks>
        /// <param name="poolBalance">The pool's balance of the asset being withdrawn, before the withdrawal.</param>
        /// <param name="withdraw">How much of it is being withdrawn.</param>
        /// <param name="lpTokenBalance">The pool's LP token balance, before the withdrawal.</param>
        /// <param name="tradingFee">The fee this account trades at - see <see cref="DiscountedTradingFee"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the amount is negative, it exceeds the pool, or the fee exceeds <see cref="TradingFeeThreshold"/>.</exception>
        public static decimal LPTokensForSingleAssetWithdraw(
            decimal poolBalance,
            decimal withdraw,
            decimal lpTokenBalance,
            uint tradingFee)
        {
            RequirePositive(poolBalance, nameof(poolBalance));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(withdraw, nameof(withdraw));
            RequireValidTradingFee(tradingFee);

            if (withdraw == 0m)
            {
                return 0m;
            }

            if (withdraw > poolBalance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(withdraw),
                    $"Cannot withdraw {withdraw} from a pool holding {poolBalance}.");
            }

            decimal fee = TradingFeeFraction(tradingFee);
            decimal fr = withdraw / poolBalance;
            decimal c = fr * fee + 2m - fee;

            return lpTokenBalance * (c - Sqrt(c * c - 4m * fr)) / 2m;
        }

        /// <summary>
        /// LP tokens credited for depositing both assets at the pool's own ratio.
        /// </summary>
        /// <remarks>
        /// A deposit in proportion does not move the price, so no fee applies: the node credits
        /// <c>T · frac</c> and takes <c>A · frac</c> and <c>B · frac</c>, where <c>frac</c> is the
        /// smaller of the two ratios offered - whichever asset runs out first decides how much of
        /// the other is used. Use <see cref="AssetsForProportionalDeposit"/> to find out how much of
        /// each will actually be taken.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, or an amount is negative.</exception>
        public static decimal LPTokensForProportionalDeposit(
            decimal poolBalance1,
            decimal poolBalance2,
            decimal deposit1,
            decimal deposit2,
            decimal lpTokenBalance)
        {
            RequirePositive(poolBalance1, nameof(poolBalance1));
            RequirePositive(poolBalance2, nameof(poolBalance2));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(deposit1, nameof(deposit1));
            RequireNotNegative(deposit2, nameof(deposit2));

            decimal frac = Math.Min(deposit1 / poolBalance1, deposit2 / poolBalance2);
            return lpTokenBalance * frac;
        }

        /// <summary>
        /// How much of each asset a proportional deposit will actually take.
        /// </summary>
        /// <remarks>
        /// The leftover of the more plentiful asset stays where it is; the node deposits both sides
        /// at the same fraction of the pool.
        /// </remarks>
        public static (decimal Asset1, decimal Asset2) AssetsForProportionalDeposit(
            decimal poolBalance1,
            decimal poolBalance2,
            decimal deposit1,
            decimal deposit2)
        {
            RequirePositive(poolBalance1, nameof(poolBalance1));
            RequirePositive(poolBalance2, nameof(poolBalance2));
            RequireNotNegative(deposit1, nameof(deposit1));
            RequireNotNegative(deposit2, nameof(deposit2));

            decimal frac = Math.Min(deposit1 / poolBalance1, deposit2 / poolBalance2);
            return (poolBalance1 * frac, poolBalance2 * frac);
        }

        /// <summary>
        /// What redeeming LP tokens returns when both assets are taken out at the pool's ratio.
        /// </summary>
        /// <remarks>
        /// Equations 1 and 2: <c>a = (t/T)·A</c> and <c>b = (t/T)·B</c>. No fee, for the same
        /// reason as a proportional deposit - the price does not move.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">A balance is not positive, the token amount is negative, or it exceeds the pool's.</exception>
        public static (decimal Asset1, decimal Asset2) AssetsForProportionalWithdraw(
            decimal poolBalance1,
            decimal poolBalance2,
            decimal lpTokens,
            decimal lpTokenBalance)
        {
            RequirePositive(poolBalance1, nameof(poolBalance1));
            RequirePositive(poolBalance2, nameof(poolBalance2));
            RequirePositive(lpTokenBalance, nameof(lpTokenBalance));
            RequireNotNegative(lpTokens, nameof(lpTokens));

            if (lpTokens > lpTokenBalance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lpTokens),
                    $"Cannot redeem {lpTokens} tokens against a balance of {lpTokenBalance}.");
            }

            decimal frac = lpTokens / lpTokenBalance;
            return (poolBalance1 * frac, poolBalance2 * frac);
        }

        /// <summary>
        /// Square root in <see cref="decimal"/>, by Newton's method.
        /// </summary>
        /// <remarks>
        /// <see cref="Math.Sqrt"/> works in <see cref="double"/>, whose 15 significant digits would
        /// discard the precision the rest of this class keeps. The iteration is seeded from the
        /// double result, which is already close, so it converges in a handful of steps; it stops
        /// when the estimate settles or begins alternating between two neighbouring values, which
        /// is how a decimal iteration ends when it can get no closer.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
        internal static decimal Sqrt(decimal value)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cannot take the square root of a negative number.");
            }

            if (value == 0m)
            {
                return 0m;
            }

            decimal guess;
            try
            {
                guess = (decimal)Math.Sqrt((double)value);
            }
            catch (OverflowException)
            {
                guess = value;
            }

            if (guess <= 0m)
            {
                guess = value > 1m ? value / 2m : 1m;
            }

            decimal previous = 0m;
            for (int step = 0; step < 100; step++)
            {
                decimal next = (guess + value / guess) / 2m;
                if (next == guess || next == previous)
                {
                    return next;
                }

                previous = guess;
                guess = next;
            }

            return guess;
        }

        private static void RequireValidTradingFee(uint tradingFee)
        {
            if (tradingFee > TradingFeeThreshold)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tradingFee),
                    $"A trading fee is in units of 1/{TradingFeeScale}, so it cannot exceed " +
                    $"{TradingFeeThreshold} - one per cent - but was {tradingFee}.");
            }
        }

        private static void RequirePositive(decimal value, string name)
        {
            if (value <= 0m)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be positive, but was {value}.");
            }
        }

        private static void RequireNotNegative(decimal value, string name)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must not be negative, but was {value}.");
            }
        }
    }
}
