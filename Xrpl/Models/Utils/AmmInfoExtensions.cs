using System;
using System.Collections.Generic;
using System.Linq;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Ledger;
using Xrpl.Utils;

namespace Xrpl.Models.Utils
{
    /// <summary>
    /// Holds the result of an AMM swap operation, including the amounts in/out,
    /// the fee charged, and the updated pool state after the swap.
    /// </summary>
    public class AmmSwapResult
    {
        /// <summary>Amount of tokens sent into the pool.</summary>
        public decimal AmountIn { get; set; }
        /// <summary>Amount of tokens received from the pool.</summary>
        public decimal AmountOut { get; set; }
        /// <summary>Trading fee deducted from the input amount.</summary>
        public decimal Fee { get; set; }
        /// <summary>Pool state after the swap (deep copy; original is not mutated).</summary>
        public AMMInfo UpdatedPool { get; set; }
    }

    /// <summary>
    /// Extension methods for <see cref="AMMInfo"/> providing AMM pool calculations
    /// for constant-product (50/50) automated market makers on the XRP Ledger.
    /// <para>
    /// Formulas (in terms of Amount/Amount2):
    /// <list type="bullet">
    ///   <item><description>Price: price(Amount2 in Amount) = Amount / Amount2</description></item>
    ///   <item><description>Invariant: k = Amount * Amount2</description></item>
    ///   <item><description>LP single-sided deposit: L = T * (sqrt(1 + B_eff/P) - 1), where B_eff = B - F*(1-W)*B</description></item>
    ///   <item><description>LP dual deposit (general): L = T * (sqrt((1+dA/A)*(1+dA2/A2)) - 1)</description></item>
    ///   <item><description>LP dual deposit (proportional): L = T * min(dA/A, dA2/A2)</description></item>
    ///   <item><description>Redeem dual: proportional share (l/T) of (Amount, Amount2)</description></item>
    ///   <item><description>Redeem single: proportional share + internal swap with trading fee</description></item>
    /// </list>
    /// </para>
    /// <para>Reference: https://xrpl.org/docs/concepts/defi/amm</para>
    /// </summary>
    public static class AmmInfoExtensions
    {
        private const decimal W = 0.5m;

        /// <summary>
        /// Converts a trading fee from basis points to a decimal fraction (0..1).
        /// XRPL uses a scale of 0–100,000 where 1,000 = 1%.
        /// For example, 795 → 0.00795.
        /// </summary>
        /// <param name="feeBps">Trading fee in basis points (0–100,000).</param>
        /// <returns>Fee as a decimal fraction between 0 and 1.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the fee exceeds 1 (100,000 bps).</exception>
        public static decimal FeeBpsToDecimal(uint feeBps)
        {
            var f = feeBps / 100000m;
            if (f > 1)
                throw new ArgumentOutOfRangeException(nameof(feeBps), "Fee must be <= 100000 bps (1%).");
            return f;
        }

        /// <summary>
        /// Calculates the price of one unit of Amount2, expressed in Amount (Amount / Amount2).
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <returns>A <see cref="Currency"/> representing the price.</returns>
        public static Currency PriceForOneAmount2InAmount(this AMMInfo p)
            => CalculatePrice(p.Amount, p.Amount2);

        /// <summary>
        /// Calculates the price of one unit of Amount, expressed in Amount2 (Amount2 / Amount).
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <returns>A <see cref="Currency"/> representing the price.</returns>
        public static Currency PriceForOneAmountInAmount2(this AMMInfo p)
            => CalculatePrice(p.Amount2, p.Amount);

        /// <summary>
        /// Calculates the constant-product invariant k = Amount * Amount2.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <returns>The invariant value.</returns>
        public static decimal InvariantK(this AMMInfo p)
            => p.Amount.ValueAsNumber * p.Amount2.ValueAsNumber;

        /// <summary>
        /// Calculates LP tokens minted for a single-sided deposit into the Amount reserve.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="deltaAmount">The amount to deposit.</param>
        /// <returns>Number of LP tokens minted.</returns>
        public static decimal MintLpSingleAssetAmount(this AMMInfo p, Currency deltaAmount)
            => MintLpSingleAsset(
                p.LPTokenBalance.ValueAsNumber,
                p.Amount.ValueAsNumber,
                deltaAmount.ValueAsNumber,
                p.TradingFee);

        /// <summary>
        /// Calculates LP tokens minted for a single-sided deposit into the Amount2 reserve.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="deltaAmount2">The amount to deposit.</param>
        /// <returns>Number of LP tokens minted.</returns>
        public static decimal MintLpSingleAssetAmount2(this AMMInfo p, Currency deltaAmount2)
            => MintLpSingleAsset(
                p.LPTokenBalance.ValueAsNumber,
                p.Amount2.ValueAsNumber,
                deltaAmount2.ValueAsNumber,
                p.TradingFee);

        /// <summary>
        /// Calculates LP tokens minted for a dual-sided deposit (general case).
        /// If one side is zero, falls back to single-sided deposit.
        /// Formula: L = T * (sqrt((1 + dA/A) * (1 + dA2/A2)) - 1)
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="dAmount">The Amount-side deposit.</param>
        /// <param name="dAmount2">The Amount2-side deposit.</param>
        /// <returns>Number of LP tokens minted.</returns>
        public static decimal MintLpDual(this AMMInfo p, Currency dAmount, Currency dAmount2)
        {
            if (dAmount.ValueAsNumber <= 0 && dAmount2.ValueAsNumber <= 0)
                return 0;
            if (dAmount.ValueAsNumber <= 0)
                return p.MintLpSingleAssetAmount2(dAmount2);
            if (dAmount2.ValueAsNumber <= 0)
                return p.MintLpSingleAssetAmount(dAmount);

            var term = DecimalMath.Sqrt(
                (1 + dAmount.ValueAsNumber / p.Amount.ValueAsNumber) *
                (1 + dAmount2.ValueAsNumber / p.Amount2.ValueAsNumber)) - 1m;
            var lp = p.LPTokenBalance.ValueAsNumber * term;
            lp = lp.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
            return lp <= 0 ? 0 : lp;
        }

        /// <summary>
        /// Calculates LP tokens minted for a proportional dual-sided deposit.
        /// Formula: L = T * min(dA/A, dA2/A2)
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="dAmount">The Amount-side deposit.</param>
        /// <param name="dAmount2">The Amount2-side deposit.</param>
        /// <returns>Number of LP tokens minted.</returns>
        public static decimal MintLpDualProportional(this AMMInfo p, Currency dAmount, Currency dAmount2)
        {
            if (dAmount.ValueAsNumber <= 0 || dAmount2.ValueAsNumber <= 0)
                return 0;
            var s1 = dAmount.ValueAsNumber / p.Amount.ValueAsNumber;
            var s2 = dAmount2.ValueAsNumber / p.Amount2.ValueAsNumber;
            var s = Math.Min(s1, s2);
            var lp = p.LPTokenBalance.ValueAsNumber * s;
            lp = lp.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
            return lp <= 0 ? 0 : lp;
        }

        /// <summary>
        /// Proportional redemption of both assets: returns (l/T) share of (Amount, Amount2).
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="l">Number of LP tokens to burn.</param>
        /// <returns>A tuple of the Amount and Amount2 received.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if l exceeds total LP supply.</exception>
        public static (Currency dAmount, Currency dAmount2) RedeemDual(this AMMInfo p, decimal l)
        {
            var amount = new Currency
            {
                CurrencyCode = p.Amount.CurrencyCode,
                Issuer = p.Amount.Issuer,
            };
            var amount2 = new Currency
            {
                CurrencyCode = p.Amount2.CurrencyCode,
                Issuer = p.Amount2.Issuer,
            };
            if (l <= 0)
                return (amount, amount2);
            if (l > p.LPTokenBalance.ValueAsNumber)
                throw new ArgumentOutOfRangeException(nameof(l), "Cannot burn more LP than total supply.");

            var s = l / p.LPTokenBalance.ValueAsNumber;
            amount.ValueAsNumber = p.Amount.ValueAsNumber * s;
            amount2.ValueAsNumber = p.Amount2.ValueAsNumber * s;
            amount.TruncateValue();
            amount2.TruncateValue();
            return (amount, amount2);
        }

        /// <summary>
        /// Single-sided redemption into Amount2:
        /// takes proportional share, then swaps the Amount portion into Amount2 via the remaining pool.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="l">Number of LP tokens to burn.</param>
        /// <returns>The total Amount2 received.</returns>
        public static Currency RedeemSingleToAmount2(this AMMInfo p, decimal l)
            => RedeemSingleAsset(p, l, p.Amount, p.Amount2, swapFromFirst: true);

        /// <summary>
        /// Single-sided redemption into Amount:
        /// takes proportional share, then swaps the Amount2 portion into Amount via the remaining pool.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="l">Number of LP tokens to burn.</param>
        /// <returns>The total Amount received.</returns>
        public static Currency RedeemSingleToAmount(this AMMInfo p, decimal l)
            => RedeemSingleAsset(p, l, p.Amount2, p.Amount, swapFromFirst: false);

        /// <summary>
        /// Calculates how many LP tokens must be burned to receive exactly
        /// <paramref name="desiredAmountOut"/> tokens via single-sided redemption.
        /// Uses an analytical solution derived from the quadratic equation of constant-product swap with fee.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="desiredAmountOut">The exact amount of tokens desired, with matching CurrencyCode and Issuer.</param>
        /// <returns>Number of LP tokens to burn.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the currency is not in the pool or amount is negative.</exception>
        public static decimal BurnLpForExactAmountOut(this AMMInfo p, Currency desiredAmountOut)
        {
            var poolAsset = p.Amount.CurrencyCode == desiredAmountOut.CurrencyCode
                            && p.Amount.Issuer == desiredAmountOut.Issuer
                ? p.Amount
                : p.Amount2.CurrencyCode == desiredAmountOut.CurrencyCode
                  && p.Amount2.Issuer == desiredAmountOut.Issuer
                    ? p.Amount2
                    : throw new ArgumentOutOfRangeException(
                        nameof(desiredAmountOut),
                        $"Currency {desiredAmountOut.CurrencyValidName} not found in current AMM.");

            if (desiredAmountOut.ValueAsNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(desiredAmountOut));

            var x = poolAsset.ValueAsNumber;
            var t = p.LPTokenBalance.ValueAsNumber;
            if (x <= 0 || t <= 0)
                return 0m;
            if (desiredAmountOut.ValueAsNumber == 0)
                return 0m;
            if (desiredAmountOut.ValueAsNumber >= x)
                return t;

            var f = FeeBpsToDecimal(p.TradingFee);
            var r = 1m - desiredAmountOut.ValueAsNumber / x;
            var rf = r * f;
            var radicand = 4m * r * (1m - f) + rf * rf;
            var sqrt = DecimalMath.Sqrt(radicand);
            var s = (2m - rf - sqrt) / 2m;

            if (s <= 0) return 0m;
            if (s >= 1) return t;

            var lp = s * t;
            lp = lp.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
            return lp > 0 ? lp : 0m;
        }

        /// <summary>
        /// Returns the raw spot price ratio: Amount.ValueAsNumber / Amount2.ValueAsNumber.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <returns>The spot price as a decimal.</returns>
        public static decimal SpotPrice(this AMMInfo p)
            => p.Amount2.ValueAsNumber != 0 ? p.Amount.ValueAsNumber / p.Amount2.ValueAsNumber : 0;

        /// <summary>
        /// Returns the AMM effective price in the same units as order book quality.
        /// For a book where taker_gets = Amount and taker_pays = Amount2:
        /// price = Amount2.ValueAsNumber / Amount.ValueAsNumber.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <returns>The effective AMM price as a decimal.</returns>
        public static decimal GetEffectiveAmmPrice(this AMMInfo p)
            => p.Amount.ValueAsNumber != 0 ? p.Amount2.ValueAsNumber / p.Amount.ValueAsNumber : 0;

        /// <summary>
        /// Sells <paramref name="deltaAmount1"/> of Amount into the pool and receives Amount2.
        /// Uses the constant-product formula with trading fee.
        /// The original pool is not mutated.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="deltaAmount1">Amount of the first asset to sell into the pool.</param>
        /// <returns>An <see cref="AmmSwapResult"/> with amounts, fee, and updated pool state.</returns>
        public static AmmSwapResult SwapAmount1ForAmount2(this AMMInfo p, decimal deltaAmount1)
        {
            var f = FeeBpsToDecimal(p.TradingFee);
            var fee = deltaAmount1 * f;
            var effectiveIn = deltaAmount1 - fee;
            var reserveIn = p.Amount.ValueAsNumber;
            var reserveOut = p.Amount2.ValueAsNumber;
            var amountOut = reserveOut * effectiveIn / (reserveIn + effectiveIn);

            var clone = ClonePool(p);
            clone.Amount.ValueAsNumber = reserveIn + deltaAmount1;
            clone.Amount2.ValueAsNumber = reserveOut - amountOut;

            return new AmmSwapResult
            {
                AmountIn = deltaAmount1,
                AmountOut = amountOut,
                Fee = fee,
                UpdatedPool = clone,
            };
        }

        /// <summary>
        /// Sells <paramref name="deltaAmount2"/> of Amount2 into the pool and receives Amount.
        /// Uses the constant-product formula with trading fee.
        /// The original pool is not mutated.
        /// </summary>
        /// <param name="p">The AMM pool info.</param>
        /// <param name="deltaAmount2">Amount of the second asset to sell into the pool.</param>
        /// <returns>An <see cref="AmmSwapResult"/> with amounts, fee, and updated pool state.</returns>
        public static AmmSwapResult SwapAmount2ForAmount1(this AMMInfo p, decimal deltaAmount2)
        {
            var f = FeeBpsToDecimal(p.TradingFee);
            var fee = deltaAmount2 * f;
            var effectiveIn = deltaAmount2 - fee;
            var reserveIn = p.Amount2.ValueAsNumber;
            var reserveOut = p.Amount.ValueAsNumber;
            var amountOut = reserveOut * effectiveIn / (reserveIn + effectiveIn);

            var clone = ClonePool(p);
            clone.Amount2.ValueAsNumber = reserveIn + deltaAmount2;
            clone.Amount.ValueAsNumber = reserveOut - amountOut;

            return new AmmSwapResult
            {
                AmountIn = deltaAmount2,
                AmountOut = amountOut,
                Fee = fee,
                UpdatedPool = clone,
            };
        }

        // ========= Private helpers =========

        /// <summary>
        /// Creates a deep copy of an <see cref="AMMInfo"/> instance, cloning Amount, Amount2,
        /// LPTokenBalance, and TradingFee so that mutations do not affect the original.
        /// </summary>
        internal static AMMInfo ClonePool(AMMInfo p)
        {
            return new AMMInfo
            {
                Account = p.Account,
                Amount = new Currency
                {
                    CurrencyCode = p.Amount.CurrencyCode,
                    Issuer = p.Amount.Issuer,
                    Value = p.Amount.Value,
                },
                Amount2 = new Currency
                {
                    CurrencyCode = p.Amount2.CurrencyCode,
                    Issuer = p.Amount2.Issuer,
                    Value = p.Amount2.Value,
                },
                LPTokenBalance = new Currency
                {
                    CurrencyCode = p.LPTokenBalance.CurrencyCode,
                    Issuer = p.LPTokenBalance.Issuer,
                    Value = p.LPTokenBalance.Value,
                },
                TradingFee = p.TradingFee,
                AuctionSlot = p.AuctionSlot,
                VoteSlots = p.VoteSlots,
                AssetFrozen = p.AssetFrozen,
                Asset2Frozen = p.Asset2Frozen,
            };
        }

        private static Currency CalculatePrice(Currency numerator, Currency denominator)
        {
            var value = numerator.GetValue() / denominator.GetValue();
            var result = new Currency
            {
                CurrencyCode = numerator.CurrencyCode,
                Issuer = numerator.Issuer,
            };
            if (numerator.IsXrp())
                result.ValueAsXrp = value.RoundTokenAmount(true, PrecisionRoundingMode.Truncate);
            else
                result.ValueAsNumber = value!.Value.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
            return result;
        }

        private static decimal MintLpSingleAsset(
            decimal totalLpT,
            decimal poolReserveP,
            decimal depositAmountB,
            uint feeBps)
        {
            if (totalLpT <= 0) return 0;
            if (poolReserveP <= 0)
                throw new ArgumentOutOfRangeException(nameof(poolReserveP));
            if (depositAmountB <= 0) return 0;

            var f = FeeBpsToDecimal(feeBps);
            var bEff = depositAmountB - f * (1 - W) * depositAmountB;
            var ratio = bEff / poolReserveP;
            var sqrtTerm = DecimalMath.Sqrt(1m + ratio) - 1m;
            var lp = totalLpT * sqrtTerm;
            lp = lp.RoundTokenAmount(false, PrecisionRoundingMode.Truncate);
            return lp <= 0 ? 0 : lp;
        }

        private static Currency RedeemSingleAsset(
            AMMInfo p,
            decimal l,
            Currency swapAsset,
            Currency targetAsset,
            bool swapFromFirst)
        {
            var result = new Currency
            {
                CurrencyCode = targetAsset.CurrencyCode,
                Issuer = targetAsset.Issuer,
            };
            if (l <= 0) return result;
            if (l > p.LPTokenBalance.ValueAsNumber)
                throw new ArgumentOutOfRangeException(nameof(l));

            var s = l / p.LPTokenBalance.ValueAsNumber;
            var dSwap = swapAsset.ValueAsNumber * s;
            var dTarget = targetAsset.ValueAsNumber * s;

            var swapRemain = swapAsset.ValueAsNumber - dSwap;
            var targetRemain = targetAsset.ValueAsNumber - dTarget;

            var f = FeeBpsToDecimal(p.TradingFee);
            var swapInEff = dSwap * (1 - f);

            var k = swapRemain * targetRemain;
            var targetOutFromSwap = targetRemain - k / (swapRemain + swapInEff);

            var totalOut = dTarget + targetOutFromSwap;
            if (totalOut <= 0) totalOut = 0;

            result.ValueAsNumber = totalOut;
            result.TruncateValue();
            return result;
        }
    }
}
