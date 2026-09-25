using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Methods;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// An AMM pool's state as <c>amm_info</c> reports it: the two assets, the balances the AMM
    /// account holds, the LP token balance and the trading fee.
    /// </summary>
    public sealed class AmmPool
    {
        /// <summary>The first asset (<c>amount</c>).</summary>
        public IssuedCurrency Asset { get; init; }

        /// <summary>The second asset (<c>amount2</c>).</summary>
        public IssuedCurrency Asset2 { get; init; }

        /// <summary>The pool's balance of <see cref="Asset"/>, in its own unit (drops for XRP).</summary>
        public XrplNumber Balance { get; init; }

        /// <summary>The pool's balance of <see cref="Asset2"/>, in its own unit (drops for XRP).</summary>
        public XrplNumber Balance2 { get; init; }

        /// <summary>The LP tokens outstanding (<c>lp_token</c>).</summary>
        public XrplNumber LpTokenBalance { get; init; }

        /// <summary>
        /// The trading fee the node applies to this account, in 1/100,000: the pool's
        /// <c>trading_fee</c>, or the auction slot's discounted fee for the slot holder
        /// (<see cref="AmmMath.DiscountedTradingFee"/>).
        /// </summary>
        public ushort TradingFee { get; init; }

        /// <summary>
        /// The pool from an <c>amm_info</c> result, at the trading fee the node applies to
        /// <paramref name="account"/> (<c>getTradingFee</c>): the auction slot's discounted fee
        /// while the slot has not expired and the account holds it or is one of its authorized
        /// accounts, and the pool's trading fee otherwise.
        /// </summary>
        /// <param name="amm">The <c>amm_info</c> result.</param>
        /// <param name="account">The account that deposits or withdraws; null takes the pool's fee.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in, against which the
        /// slot's expiration is checked; null takes the current time.
        /// </param>
        /// <exception cref="ArgumentException">The result carries no pool.</exception>
        public static AmmPool FromAmmInfo(AMMInfo amm, string account = null, DateTime? parentCloseTime = null)
        {
            if (amm?.Amount == null || amm.Amount2 == null || amm.LPTokenBalance == null)
                throw new ArgumentException("The amm_info result carries no pool.", nameof(amm));

            return new AmmPool
            {
                Asset = AssetOf(amm.Amount),
                Asset2 = AssetOf(amm.Amount2),
                Balance = XrplNumber.Parse(amm.Amount.Value),
                Balance2 = XrplNumber.Parse(amm.Amount2.Value),
                LpTokenBalance = XrplNumber.Parse(amm.LPTokenBalance.Value),
                TradingFee = checked((ushort)FeeFor(amm, account, parentCloseTime ?? DateTime.UtcNow)),
            };
        }

        /// <summary><c>getTradingFee</c>.</summary>
        private static uint FeeFor(AMMInfo amm, string account, DateTime at)
        {
            Models.Ledger.AuctionSlot slot = amm.AuctionSlot;
            if (account == null || slot?.Expiration is not { } expiration)
                return amm.TradingFee;

            // The slot is live while the parent close time is before its expiration, to the second.
            if (LendingMath.RippleSeconds(at) >= LendingMath.RippleSeconds(expiration))
                return amm.TradingFee;

            if (string.Equals(slot.Account, account, StringComparison.Ordinal))
                return slot.DiscountedFee;

            if (slot.AuthAccounts != null)
            {
                foreach (Models.Ledger.AuthAccount authorized in slot.AuthAccounts)
                {
                    if (string.Equals(authorized?.Account, account, StringComparison.Ordinal))
                        return slot.DiscountedFee;
                }
            }

            return amm.TradingFee;
        }

        private static IssuedCurrency AssetOf(Models.Common.Currency amount) => amount.MPTokenIssuanceID != null
            ? new IssuedCurrency { MptIssuanceId = amount.MPTokenIssuanceID }
            : new IssuedCurrency { Currency = amount.CurrencyCode, Issuer = amount.Issuer };
    }

    /// <summary>
    /// What an AMM deposit or withdrawal would move, or why the node would refuse it. Amounts are
    /// in each asset's own unit - drops for XRP - and always positive.
    /// </summary>
    public sealed class AmmQuote
    {
        /// <summary>The amount of the pool's first asset that moves.</summary>
        public decimal Asset1 { get; init; }

        /// <summary>The amount of the pool's second asset that moves.</summary>
        public decimal Asset2 { get; init; }

        /// <summary>The LP tokens issued by a deposit or redeemed by a withdrawal.</summary>
        public decimal LpTokens { get; init; }

        /// <summary>
        /// The pool as the ledger will hold it afterwards: each balance and the LP token balance
        /// with the amount added or taken away in <c>STAmount</c> arithmetic. An issued-currency
        /// balance keeps 16 significant digits, so the difference between two balances can round
        /// the amount that moved; these are the figures <c>amm_info</c> reports next.
        /// </summary>
        public AmmPool PoolAfter { get; init; }

        /// <summary>
        /// The result the node would give instead, such as <c>tecAMM_INVALID_TOKENS</c>,
        /// <c>tecAMM_FAILED</c>, <c>tecAMM_BALANCE</c> or <c>tecPRECISION_LOSS</c>; null when these
        /// checks pass.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when these checks pass.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the transaction passes the checks <see cref="AmmLiquidity"/> repeats.</summary>
        public bool IsAccepted => Refusal == null;
    }

    /// <summary>
    /// AMM deposits and withdrawals computed exactly, the way rippled's <c>AMMDeposit</c> and
    /// <c>AMMWithdraw</c> compute them under <c>fixAMMv1_3</c>: the LP tokens a deposit issues, the
    /// assets a withdrawal pays, and the refusals the amounts lead to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where <see cref="AmmMath"/> gives a close <see cref="decimal"/> estimate, this follows the
    /// node's <c>Number</c> and <c>STAmount</c> arithmetic step by step, rounding included, so a
    /// quote is what the node moves. Read the pool with <c>amm_info</c> immediately before quoting.
    /// </para>
    /// <para>
    /// Covered are every deposit mode - <c>tfLPToken</c>, <c>tfTwoAsset</c>, <c>tfSingleAsset</c>,
    /// <c>tfOneAssetLPToken</c>, <c>tfLimitLPToken</c> and <c>tfTwoAssetIfEmpty</c> - and every
    /// withdrawal mode - <c>tfLPToken</c>, <c>tfWithdrawAll</c>, <c>tfTwoAsset</c>,
    /// <c>tfSingleAsset</c>, <c>tfOneAssetLPToken</c>, <c>tfOneAssetWithdrawAll</c> and
    /// <c>tfLimitLPToken</c>. Not covered: the transaction's own validation (such as an amount above
    /// the pool's balance, <c>tecAMM_BALANCE</c> in preclaim), and the checks on the account
    /// itself - its funds, reserve, authorization and freezes - which
    /// <see cref="PreviewSugar.PreviewAMMDeposit"/> and <see cref="PreviewSugar.PreviewAMMWithdraw"/> cover.
    /// </para>
    /// </remarks>
    public static class AmmLiquidity
    {
        /// <summary>A proportional deposit for <paramref name="lpTokens"/> LP tokens (<c>tfLPToken</c>).</summary>
        /// <param name="pool">The pool.</param>
        /// <param name="lpTokens">The LP tokens to receive.</param>
        /// <param name="amount">The transaction's <c>Amount</c>, if any: the first asset may not come out below it.</param>
        /// <param name="amount2">The transaction's <c>Amount2</c>, if any: the second asset may not come out below it.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote DepositForTokens(AmmPool pool, XrplNumber lpTokens, XrplNumber? amount = null, XrplNumber? amount2 = null, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, null, rules);
            NumberContext c = p.Context;
            XrplNumber tokens = AmmFormulas.AdjustTokens(p.LpTokens, Tokens(lpTokens, c), isDeposit: true, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The LP tokens round to zero against the pool's balance.");

            XrplNumber fraction = AmountMath.Divide(tokens, AmountKind.Iou, p.LpTokens, AmountKind.Iou, c);
            XrplNumber deposit1 = AmmFormulas.RoundedAsset(p.Balance1, p.Kind1, fraction, isDeposit: true, c);
            XrplNumber deposit2 = AmmFormulas.RoundedAsset(p.Balance2, p.Kind2, fraction, isDeposit: true, c);
            return Deposit(p, deposit1, deposit2, tokens, amount, amount2, null);
        }

        /// <summary>
        /// A proportional deposit of at most <paramref name="amount"/> and <paramref name="amount2"/>
        /// (<c>tfTwoAsset</c>): one of them is deposited in full and the other in proportion.
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="amount">The most of the pool's first asset to deposit.</param>
        /// <param name="amount2">The most of the pool's second asset to deposit.</param>
        /// <param name="minLpTokens">The transaction's <c>LPTokenOut</c>, if any: the least LP tokens to accept.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote DepositBoth(AmmPool pool, XrplNumber amount, XrplNumber amount2, XrplNumber? minLpTokens = null, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, null, rules);
            NumberContext c = p.Context;
            XrplNumber a1 = AmountMath.ToAmount(amount, p.Kind1, c);
            XrplNumber a2 = AmountMath.ToAmount(amount2, p.Kind2, c);

            XrplNumber fraction = XrplNumber.Divide(a1, p.Balance1, c);
            XrplNumber tokens = AmmFormulas.RoundedTokens(p.LpTokens, fraction, isDeposit: true, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

            fraction = XrplNumber.Divide(tokens, p.LpTokens, c);
            XrplNumber deposit2 = AmmFormulas.RoundedAsset(p.Balance2, p.Kind2, fraction, isDeposit: true, c);
            if (deposit2 <= a2)
                return Deposit(p, a1, deposit2, tokens, null, null, minLpTokens);

            fraction = XrplNumber.Divide(a2, p.Balance2, c);
            tokens = AmmFormulas.RoundedTokens(p.LpTokens, fraction, isDeposit: true, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

            fraction = XrplNumber.Divide(tokens, p.LpTokens, c);
            XrplNumber deposit1 = AmmFormulas.RoundedAsset(p.Balance1, p.Kind1, fraction, isDeposit: true, c);
            if (deposit1 <= a1)
                return Deposit(p, deposit1, a2, tokens, null, null, minLpTokens);

            return Refused("tecAMM_FAILED", "Neither amount can be matched in proportion within the other.");
        }

        /// <summary>A single-asset deposit of <paramref name="amount"/> (<c>tfSingleAsset</c>).</summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to deposit.</param>
        /// <param name="amount">The amount to deposit.</param>
        /// <param name="minLpTokens">The transaction's <c>LPTokenOut</c>, if any: the least LP tokens to accept.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote DepositSingle(AmmPool pool, IssuedCurrency asset, XrplNumber amount, XrplNumber? minLpTokens = null, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, asset, rules);
            NumberContext c = p.Context;
            XrplNumber a = AmountMath.ToAmount(amount, p.Kind1, c);

            XrplNumber tokens = AmmFormulas.AdjustTokens(
                p.LpTokens,
                AmmFormulas.LpTokensOut(p.Balance1, a, p.LpTokens, p.Fee, c),
                isDeposit: true,
                c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

            (XrplNumber adjustedTokens, XrplNumber deposit) = AmmFormulas.AdjustAssetIn(p.Balance1, p.Kind1, a, p.LpTokens, tokens, p.Fee, c);
            if (adjustedTokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

            return Deposit(p, deposit, null, adjustedTokens, null, null, minLpTokens);
        }

        /// <summary>
        /// A single-asset deposit for <paramref name="lpTokens"/> LP tokens, taking at most
        /// <paramref name="maxAmount"/> (<c>tfOneAssetLPToken</c>).
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to deposit.</param>
        /// <param name="maxAmount">The most of the asset to deposit.</param>
        /// <param name="lpTokens">The LP tokens to receive.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote DepositSingleForTokens(AmmPool pool, IssuedCurrency asset, XrplNumber maxAmount, XrplNumber lpTokens, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, asset, rules);
            NumberContext c = p.Context;
            XrplNumber tokens = AmmFormulas.AdjustTokens(p.LpTokens, Tokens(lpTokens, c), isDeposit: true, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The LP tokens round to zero against the pool's balance.");

            XrplNumber deposit = AmmFormulas.AssetIn(p.Balance1, p.Kind1, p.LpTokens, tokens, p.Fee, c);
            if (deposit > AmountMath.ToAmount(maxAmount, p.Kind1, c))
                return Refused("tecAMM_FAILED", "The LP tokens cost more of the asset than the most allowed.");

            return Deposit(p, deposit, null, tokens, null, null, null);
        }

        /// <summary>
        /// A single-asset deposit limited by an effective price (<c>tfLimitLPToken</c>): the
        /// deposit is at most <paramref name="amount"/> - all of it when its price per LP token
        /// stays within <paramref name="effectivePrice"/> - and otherwise the amount at which the
        /// price per LP token reaches the limit.
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to deposit.</param>
        /// <param name="amount">The transaction's <c>Amount</c>; zero deposits up to the price limit alone.</param>
        /// <param name="effectivePrice">The transaction's <c>EPrice</c>: the most of the asset to pay per LP token.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote DepositWithEffectivePrice(AmmPool pool, IssuedCurrency asset, XrplNumber amount, XrplNumber effectivePrice, LedgerRules rules = null)
        {
            if (amount < XrplNumber.Zero || effectivePrice <= XrplNumber.Zero)
                return Refused("temBAD_AMOUNT", "The amount is negative or the effective price is not positive.");

            Pool p = Pool.Of(pool, asset, rules);
            NumberContext c = p.Context;
            XrplNumber a = AmountMath.ToAmount(amount, p.Kind1, c);
            XrplNumber price = effectivePrice;

            if (!a.IsZero)
            {
                XrplNumber tokens = AmmFormulas.AdjustTokens(p.LpTokens, AmmFormulas.LpTokensOut(p.Balance1, a, p.LpTokens, p.Fee, c), isDeposit: true, c);
                if (tokens <= XrplNumber.Zero)
                    return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

                (XrplNumber adjustedTokens, XrplNumber deposit) = AmmFormulas.AdjustAssetIn(p.Balance1, p.Kind1, a, p.LpTokens, tokens, p.Fee, c);
                if (adjustedTokens.IsZero)
                    return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

                if (XrplNumber.Divide(deposit, adjustedTokens, c) <= price)
                    return Deposit(p, deposit, null, adjustedTokens, null, null, null);
            }

            // The deposit whose price per LP token equals the limit.
            XrplNumber f1 = AmmFormulas.FeeMult(p.Fee, c);
            XrplNumber f2 = XrplNumber.Divide(AmmFormulas.FeeMultHalf(p.Fee, c), f1, c);
            XrplNumber cc = XrplNumber.Divide(XrplNumber.Multiply(f1, p.Balance1, c), XrplNumber.Multiply(price, p.LpTokens, c), c);
            XrplNumber d = XrplNumber.Subtract(XrplNumber.Add(f1, XrplNumber.Multiply(cc, f2, c), c), cc, c);
            XrplNumber a1 = XrplNumber.Multiply(cc, cc, c);
            XrplNumber b1 = XrplNumber.Subtract(
                XrplNumber.Add(XrplNumber.Multiply(XrplNumber.Multiply(XrplNumber.Multiply(cc, cc, c), f2, c), f2, c), XrplNumber.Multiply(2, cc, c), c),
                XrplNumber.Multiply(d, d, c),
                c);
            XrplNumber c1 = XrplNumber.Subtract(
                XrplNumber.Add(XrplNumber.Multiply(XrplNumber.Multiply(XrplNumber.Multiply(2, cc, c), f2, c), f2, c), 1, c),
                XrplNumber.Multiply(XrplNumber.Multiply(2, d, c), f2, c),
                c);
            XrplNumber product = XrplNumber.Multiply(f1, AmmFormulas.SolveQuadratic(a1, b1, c1, c), c);
            XrplNumber amountDeposit = AmountMath.Multiply(p.Balance1, p.Kind1, product, c, NumberRounding.Upward);
            if (amountDeposit <= XrplNumber.Zero)
                return Refused("tecAMM_FAILED", "No deposit reaches the effective price.");

            // getRoundedLPTokens for a deposit: the tokens computed and converted rounding down.
            NumberContext down = c.WithRounding(NumberRounding.Downward);
            XrplNumber priced = AmountMath.ToAmount(XrplNumber.Divide(amountDeposit, price, down), AmountKind.Iou, down);
            XrplNumber limitTokens = AmmFormulas.AdjustTokens(p.LpTokens, priced, isDeposit: true, c);
            (XrplNumber tokensAdj, XrplNumber depositAdj) = AmmFormulas.AdjustAssetIn(p.Balance1, p.Kind1, amountDeposit, p.LpTokens, limitTokens, p.Fee, c);
            if (tokensAdj.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The deposit is worth less than the smallest LP token amount.");

            return Deposit(p, depositAdj, null, tokensAdj, null, null, null);
        }

        /// <summary>
        /// Deposits both assets into a pool with no LP tokens outstanding (<c>tfTwoAssetIfEmpty</c>):
        /// the LP tokens are <c>sqrt(amount * amount2)</c> rounded down, as <c>AMMCreate</c> issues them.
        /// </summary>
        /// <param name="pool">The empty pool.</param>
        /// <param name="amount">The pool's first asset to deposit.</param>
        /// <param name="amount2">The pool's second asset to deposit.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote DepositIntoEmptyPool(AmmPool pool, XrplNumber amount, XrplNumber amount2, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, null, rules);
            if (!p.LpTokens.IsZero)
                return Refused("tecAMM_NOT_EMPTY", "The pool has LP tokens outstanding; tfTwoAssetIfEmpty needs an empty pool.");

            NumberContext c = p.Context;
            XrplNumber a1 = AmountMath.ToAmount(amount, p.Kind1, c);
            XrplNumber a2 = AmountMath.ToAmount(amount2, p.Kind2, c);
            return p.EmptyDeposit(a1, a2, AmmFormulas.InitialTokens(a1, a2, c));
        }

        /// <summary>
        /// The LP tokens <c>AMMCreate</c> issues for a new pool of <paramref name="amount"/> and
        /// <paramref name="amount2"/>: <c>sqrt(amount * amount2)</c>, rounded down.
        /// </summary>
        /// <param name="amount">The first asset, in its own unit (drops for XRP).</param>
        /// <param name="amount2">The second asset, in its own unit (drops for XRP).</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static decimal InitialLpTokens(XrplNumber amount, XrplNumber amount2, LedgerRules rules = null)
        {
            rules ??= new LedgerRules();
            return LoanSchedule.ToDecimal(AmmFormulas.InitialTokens(amount, amount2, rules.Context));
        }

        /// <summary>
        /// A single-asset withdrawal limited by an effective price (<c>tfLimitLPToken</c>): the LP
        /// tokens whose redemption pays the asset at exactly <paramref name="effectivePrice"/> LP
        /// tokens per unit, as long as that pays at least <paramref name="amount"/>.
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to withdraw.</param>
        /// <param name="amount">The transaction's <c>Amount</c>: the least of the asset to accept; zero accepts any amount.</param>
        /// <param name="effectivePrice">
        /// The transaction's <c>EPrice</c>, which a withdrawal states in LP tokens: the LP tokens
        /// redeemed per unit of the asset. The asset paid is the LP tokens divided by it.
        /// </param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawWithEffectivePrice(
            AmmPool pool,
            IssuedCurrency asset,
            XrplNumber amount,
            XrplNumber effectivePrice,
            XrplNumber holderLpTokens,
            bool isOnlyLiquidityProvider = false,
            LedgerRules rules = null)
        {
            if (amount < XrplNumber.Zero || effectivePrice <= XrplNumber.Zero)
                return Refused("temBAD_AMOUNT", "The amount is negative or the effective price is not positive.");

            Pool p = Pool.Of(pool, asset, rules);
            if (p.AlignWithOnlyProvider(holderLpTokens, isOnlyLiquidityProvider) is { } invalid)
                return invalid;

            NumberContext c = p.Context;
            XrplNumber price = effectivePrice;
            XrplNumber ae = XrplNumber.Multiply(p.Balance1, price, c);
            XrplNumber f = AmmFormulas.Fee(p.Fee, c);
            XrplNumber denominator = XrplNumber.Subtract(XrplNumber.Multiply(p.LpTokens, f, c), ae, c);
            if (p.Rules.FixCleanup3_3_0 && denominator.IsZero)
                return Refused("tecAMM_FAILED", "The effective price makes the equation degenerate.");

            // getRoundedLPTokens for a withdrawal: the fraction at the current rounding, the tokens rounded up.
            XrplNumber fraction = XrplNumber.Divide(
                XrplNumber.Add(p.LpTokens, XrplNumber.Multiply(ae, XrplNumber.Subtract(f, 2, c), c), c),
                denominator,
                c);
            XrplNumber tokens = AmmFormulas.AdjustTokens(
                p.LpTokens,
                AmountMath.Multiply(p.LpTokens, AmountKind.Iou, fraction, c, NumberRounding.Upward),
                isDeposit: false,
                c);
            if (tokens <= XrplNumber.Zero)
                return Refused("tecAMM_INVALID_TOKENS", "The effective price redeems no LP tokens.");

            // getRoundedAsset for a withdrawal: the amount computed and converted rounding down.
            NumberContext down = c.WithRounding(NumberRounding.Downward);
            XrplNumber withdrawal = AmountMath.ToAmount(XrplNumber.Divide(tokens, price, down), p.Kind1, down);
            XrplNumber least = AmountMath.ToAmount(amount, p.Kind1, c);
            if (!least.IsZero && withdrawal < least)
                return Refused("tecAMM_FAILED", "The effective price pays less of the asset than the least accepted.");

            return Withdraw(p, withdrawal, null, tokens, holderLpTokens);
        }

        /// <summary>A proportional withdrawal for <paramref name="lpTokens"/> LP tokens (<c>tfLPToken</c>).</summary>
        /// <param name="pool">The pool.</param>
        /// <param name="lpTokens">The LP tokens to redeem.</param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawForTokens(AmmPool pool, XrplNumber lpTokens, XrplNumber holderLpTokens, bool isOnlyLiquidityProvider = false, LedgerRules rules = null) =>
            EqualWithdraw(pool, lpTokens, holderLpTokens, withdrawAll: false, isOnlyLiquidityProvider, rules);

        /// <summary>A proportional withdrawal of every LP token the account holds (<c>tfWithdrawAll</c>).</summary>
        /// <param name="pool">The pool.</param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawAll(AmmPool pool, XrplNumber holderLpTokens, bool isOnlyLiquidityProvider = false, LedgerRules rules = null) =>
            EqualWithdraw(pool, holderLpTokens, holderLpTokens, withdrawAll: true, isOnlyLiquidityProvider, rules);

        /// <summary>
        /// A proportional withdrawal of at most <paramref name="amount"/> and
        /// <paramref name="amount2"/> (<c>tfTwoAsset</c>).
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="amount">The most of the pool's first asset to withdraw.</param>
        /// <param name="amount2">The most of the pool's second asset to withdraw.</param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawBoth(AmmPool pool, XrplNumber amount, XrplNumber amount2, XrplNumber holderLpTokens, bool isOnlyLiquidityProvider = false, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, null, rules);
            if (p.AlignWithOnlyProvider(holderLpTokens, isOnlyLiquidityProvider) is { } invalid)
                return invalid;

            NumberContext c = p.Context;
            XrplNumber a1 = AmountMath.ToAmount(amount, p.Kind1, c);
            XrplNumber a2 = AmountMath.ToAmount(amount2, p.Kind2, c);

            XrplNumber fraction = XrplNumber.Divide(a1, p.Balance1, c);
            XrplNumber tokens = AmmFormulas.RoundedTokens(p.LpTokens, fraction, isDeposit: false, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The withdrawal is worth less than the smallest LP token amount.");

            fraction = XrplNumber.Divide(tokens, p.LpTokens, c);
            XrplNumber withdrawal2 = AmmFormulas.RoundedAsset(p.Balance2, p.Kind2, fraction, isDeposit: false, c);
            if (withdrawal2 <= a2)
                return Withdraw(p, a1, withdrawal2, tokens, holderLpTokens);

            fraction = XrplNumber.Divide(a2, p.Balance2, c);
            tokens = AmmFormulas.RoundedTokens(p.LpTokens, fraction, isDeposit: false, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The withdrawal is worth less than the smallest LP token amount.");

            fraction = XrplNumber.Divide(tokens, p.LpTokens, c);
            XrplNumber withdrawal1 = AmmFormulas.RoundedAsset(p.Balance1, p.Kind1, fraction, isDeposit: false, c);
            if (withdrawal1 > a1)
                return Refused("tecAMM_FAILED", "Neither amount can be matched in proportion within the other.");

            return Withdraw(p, withdrawal1, a2, tokens, holderLpTokens);
        }

        /// <summary>A single-asset withdrawal of <paramref name="amount"/> (<c>tfSingleAsset</c>).</summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to withdraw.</param>
        /// <param name="amount">The amount to withdraw.</param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawSingle(AmmPool pool, IssuedCurrency asset, XrplNumber amount, XrplNumber holderLpTokens, bool isOnlyLiquidityProvider = false, LedgerRules rules = null)
        {
            Pool p = Pool.Of(pool, asset, rules);
            if (p.AlignWithOnlyProvider(holderLpTokens, isOnlyLiquidityProvider) is { } invalid)
                return invalid;

            NumberContext c = p.Context;
            XrplNumber a = AmountMath.ToAmount(amount, p.Kind1, c);
            XrplNumber tokens = AmmFormulas.AdjustTokens(
                p.LpTokens,
                AmmFormulas.LpTokensIn(p.Balance1, a, p.LpTokens, p.Fee, c),
                isDeposit: false,
                c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The withdrawal is worth less than the smallest LP token amount.");

            (XrplNumber adjustedTokens, XrplNumber withdrawal) = AmmFormulas.AdjustAssetOut(p.Balance1, p.Kind1, a, p.LpTokens, tokens, p.Fee, c);
            if (adjustedTokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The withdrawal is worth less than the smallest LP token amount.");

            return Withdraw(p, withdrawal, null, adjustedTokens, holderLpTokens);
        }

        /// <summary>
        /// A single-asset withdrawal for <paramref name="lpTokens"/> LP tokens, paying at least
        /// <paramref name="minAmount"/> (<c>tfOneAssetLPToken</c>).
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to withdraw.</param>
        /// <param name="lpTokens">The LP tokens to redeem.</param>
        /// <param name="minAmount">The least of the asset to accept; zero accepts any amount.</param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawSingleForTokens(
            AmmPool pool,
            IssuedCurrency asset,
            XrplNumber lpTokens,
            XrplNumber minAmount,
            XrplNumber holderLpTokens,
            bool isOnlyLiquidityProvider = false,
            LedgerRules rules = null) =>
            SingleWithdrawTokens(pool, asset, lpTokens, minAmount, holderLpTokens, withdrawAll: false, isOnlyLiquidityProvider, rules);

        /// <summary>
        /// A single-asset withdrawal of every LP token the account holds (<c>tfOneAssetWithdrawAll</c>).
        /// </summary>
        /// <param name="pool">The pool.</param>
        /// <param name="asset">Which of the pool's assets to withdraw.</param>
        /// <param name="minAmount">The least of the asset to accept; zero accepts any amount.</param>
        /// <param name="holderLpTokens">The LP tokens the account holds.</param>
        /// <param name="isOnlyLiquidityProvider">Whether the account is the pool's only liquidity provider.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        public static AmmQuote WithdrawAllOfAsset(
            AmmPool pool,
            IssuedCurrency asset,
            XrplNumber minAmount,
            XrplNumber holderLpTokens,
            bool isOnlyLiquidityProvider = false,
            LedgerRules rules = null) =>
            SingleWithdrawTokens(pool, asset, holderLpTokens, minAmount, holderLpTokens, withdrawAll: true, isOnlyLiquidityProvider, rules);

        /// <summary><c>equalWithdrawTokens</c>.</summary>
        private static AmmQuote EqualWithdraw(AmmPool pool, XrplNumber lpTokens, XrplNumber holderLpTokens, bool withdrawAll, bool isOnlyLiquidityProvider, LedgerRules rules)
        {
            Pool p = Pool.Of(pool, null, rules);
            if (p.AlignWithOnlyProvider(holderLpTokens, isOnlyLiquidityProvider) is { } invalid)
                return invalid;

            NumberContext c = p.Context;
            XrplNumber requested = Tokens(lpTokens, c);
            if (requested == p.LpTokens)
                return Withdraw(p, p.Balance1, p.Balance2, requested, holderLpTokens);

            XrplNumber tokens = withdrawAll ? requested : AmmFormulas.AdjustTokens(p.LpTokens, requested, isDeposit: false, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The LP tokens round to zero against the pool's balance.");

            XrplNumber fraction = AmountMath.Divide(tokens, AmountKind.Iou, p.LpTokens, AmountKind.Iou, c);
            XrplNumber withdrawal1 = AmmFormulas.RoundedAsset(p.Balance1, p.Kind1, fraction, isDeposit: false, c);
            XrplNumber withdrawal2 = AmmFormulas.RoundedAsset(p.Balance2, p.Kind2, fraction, isDeposit: false, c);
            if (withdrawal1.IsZero || withdrawal2.IsZero)
                return Refused("tecAMM_FAILED", "The LP tokens are worth less than the smallest amount of one of the assets.");

            return Withdraw(p, withdrawal1, withdrawal2, tokens, holderLpTokens);
        }

        /// <summary><c>singleWithdrawTokens</c>.</summary>
        private static AmmQuote SingleWithdrawTokens(
            AmmPool pool,
            IssuedCurrency asset,
            XrplNumber lpTokens,
            XrplNumber minAmount,
            XrplNumber holderLpTokens,
            bool withdrawAll,
            bool isOnlyLiquidityProvider,
            LedgerRules rules)
        {
            Pool p = Pool.Of(pool, asset, rules);
            if (p.AlignWithOnlyProvider(holderLpTokens, isOnlyLiquidityProvider) is { } invalid)
                return invalid;

            NumberContext c = p.Context;
            XrplNumber requested = Tokens(lpTokens, c);
            XrplNumber tokens = withdrawAll ? requested : AmmFormulas.AdjustTokens(p.LpTokens, requested, isDeposit: false, c);
            if (tokens.IsZero)
                return Refused("tecAMM_INVALID_TOKENS", "The LP tokens round to zero against the pool's balance.");

            XrplNumber withdrawal = AmmFormulas.AssetOut(p.Balance1, p.Kind1, p.LpTokens, tokens, p.Fee, c);
            XrplNumber least = AmountMath.ToAmount(minAmount, p.Kind1, c);
            if (!least.IsZero && withdrawal < least)
                return Refused("tecAMM_FAILED", "The LP tokens pay less of the asset than the least accepted.");

            return Withdraw(p, withdrawal, null, tokens, holderLpTokens);
        }

        /// <summary><c>AMMDeposit::deposit</c>, then the pool invariant check.</summary>
        private static AmmQuote Deposit(
            Pool p,
            XrplNumber deposit1,
            XrplNumber? deposit2,
            XrplNumber tokens,
            XrplNumber? min1,
            XrplNumber? min2,
            XrplNumber? minTokens)
        {
            NumberContext c = p.Context;
            if (tokens <= XrplNumber.Zero)
                return Refused("tecAMM_INVALID_TOKENS", "The deposit issues no LP tokens.");
            if ((min1 is { } m1 && deposit1 < m1)
                || (min2 is { } m2 && deposit2 is { } d2 && d2 < m2)
                || (minTokens is { } mt && tokens < mt))
            {
                return Refused("tecAMM_FAILED", "The deposit falls below a minimum the transaction sets.");
            }

            if (deposit1 <= XrplNumber.Zero || (deposit2 is { } second && second <= XrplNumber.Zero))
                return Refused("temBAD_AMOUNT", "The deposit comes out as zero of an asset.");

            XrplNumber newTokens = AmountMath.Add(p.LpTokens, tokens, AmountKind.Iou, c);
            XrplNumber pool1 = AmountMath.Add(p.Balance1, deposit1, p.Kind1, c);
            XrplNumber pool2 = deposit2 is { } add2 ? AmountMath.Add(p.Balance2, add2, p.Kind2, c) : p.Balance2;
            if (p.ChecksPrecision && !AmmFormulas.KeepsInvariant(pool1, pool2, newTokens, c))
                return Refused("tecPRECISION_LOSS", "The pool's geometric mean would fall below its LP token balance.");

            return p.Quote(deposit1, deposit2 ?? XrplNumber.Zero, tokens, pool1, pool2, newTokens);
        }

        /// <summary><c>AMMWithdraw::withdraw</c>, then the pool invariant check.</summary>
        private static AmmQuote Withdraw(Pool p, XrplNumber withdrawal1, XrplNumber? withdrawal2, XrplNumber tokens, XrplNumber holderLpTokens)
        {
            NumberContext c = p.Context;
            if (tokens <= XrplNumber.Zero || tokens > holderLpTokens)
                return Refused("tecAMM_INVALID_TOKENS", "The withdrawal redeems no LP tokens, or more than the account holds.");
            if (p.Rules.FixAMMv1_1 && tokens > p.LpTokens)
                return Refused("tecINTERNAL", "The withdrawal redeems more LP tokens than the pool has.");

            bool all1 = withdrawal1 == p.Balance1;
            bool all2 = withdrawal2 is { } w2 && w2 == p.Balance2;
            if ((all1 && !all2) || (all2 && !all1))
                return Refused("tecAMM_BALANCE", "The withdrawal empties one side of the pool but not the other.");
            if (tokens == p.LpTokens && (!all1 || !all2))
                return Refused("tecAMM_BALANCE", "The withdrawal redeems every LP token but leaves assets in the pool.");
            if (withdrawal1 > p.Balance1 || (withdrawal2 is { } over2 && over2 > p.Balance2))
                return Refused("tecAMM_BALANCE", "The withdrawal takes more than the pool holds.");

            XrplNumber pool1 = AmountMath.Subtract(p.Balance1, withdrawal1, p.Kind1, c);
            XrplNumber pool2 = withdrawal2 is { } take2 ? AmountMath.Subtract(p.Balance2, take2, p.Kind2, c) : p.Balance2;
            XrplNumber newTokens = AmountMath.Subtract(p.LpTokens, tokens, AmountKind.Iou, c);
            if (p.Rules.MPTokensV2)
            {
                bool valid = withdrawal2 == null
                    ? pool1.IsZero == newTokens.IsZero
                    : pool1.IsZero == pool2.IsZero && pool2.IsZero == newTokens.IsZero;
                if (!valid)
                    return Refused("tecAMM_BALANCE", "The withdrawal would leave the pool's sides and LP tokens inconsistent.");
            }

            if (p.ChecksPrecision && !AmmFormulas.KeepsInvariant(pool1, pool2, newTokens, c))
                return Refused("tecPRECISION_LOSS", "The pool's geometric mean would fall below its LP token balance.");

            return p.Quote(withdrawal1, withdrawal2 ?? XrplNumber.Zero, tokens, pool1, pool2, newTokens);
        }

        /// <summary>LP tokens as an amount: an IOU rounded to 16 digits.</summary>
        private static XrplNumber Tokens(XrplNumber value, NumberContext c) => AmountMath.ToAmount(value, AmountKind.Iou, c);

        private static AmmQuote Refused(string result, string reason) =>
            new AmmQuote { Refusal = result, RefusalReason = reason };

        /// <summary>The pool as one operation sees it: the named asset first.</summary>
        private sealed class Pool
        {
            private bool _swapped;

            private AmmPool _original;

            public LedgerRules Rules { get; private init; }

            public NumberContext Context { get; private init; }

            public XrplNumber Balance1 { get; private init; }

            public XrplNumber Balance2 { get; private init; }

            public AmountKind Kind1 { get; private init; }

            public AmountKind Kind2 { get; private init; }

            public XrplNumber LpTokens { get; private set; }

            public ushort Fee { get; private init; }

            /// <summary>Whether <c>checkAMMPrecisionLoss</c> runs: under <c>fixCleanup3_3_0</c> and <c>fixAMMv1_3</c>.</summary>
            public bool ChecksPrecision => Rules.FixCleanup3_3_0 && Rules.FixAMMv1_3;

            public static Pool Of(AmmPool pool, IssuedCurrency first, LedgerRules rules)
            {
                if (pool == null)
                    throw new ArgumentNullException(nameof(pool));

                rules ??= new LedgerRules();
                if (!rules.FixAMMv1_3)
                    throw new NotSupportedException("AmmLiquidity follows the rules of fixAMMv1_3, which the ledger does not have.");

                bool swapped = first != null && !SameAsset(first, pool.Asset);
                if (swapped && !SameAsset(first, pool.Asset2))
                    throw new ArgumentException("The asset is not one of the pool's.", nameof(first));

                NumberContext c = rules.Context;
                return new Pool
                {
                    _swapped = swapped,
                    _original = pool,
                    Rules = rules,
                    Context = c,
                    Balance1 = swapped ? pool.Balance2 : pool.Balance,
                    Balance2 = swapped ? pool.Balance : pool.Balance2,
                    Kind1 = KindOf(swapped ? pool.Asset2 : pool.Asset),
                    Kind2 = KindOf(swapped ? pool.Asset : pool.Asset2),
                    LpTokens = pool.LpTokenBalance,
                    Fee = pool.TradingFee,
                };
            }

            /// <summary>
            /// <c>verifyAndAdjustLPTokenBalance</c>: for the only liquidity provider, the pool's LP
            /// token balance is taken as the provider's when the two are within 0.1%.
            /// </summary>
            public AmmQuote AlignWithOnlyProvider(XrplNumber holderLpTokens, bool isOnlyLiquidityProvider)
            {
                if (!Rules.FixAMMv1_1 || !isOnlyLiquidityProvider)
                    return null;

                if (!AmmFormulas.WithinRelativeDistance(holderLpTokens, LpTokens, new XrplNumber(1, -3), Context))
                    return Refused("tecAMM_INVALID_TOKENS", "The only provider's LP tokens differ from the pool's balance by more than 0.1%.");

                LpTokens = holderLpTokens;
                return null;
            }

            /// <summary>
            /// <c>equalDepositInEmptyState</c>: <c>deposit</c> against a pool with no LP tokens, whose
            /// balances are the amounts deposited.
            /// </summary>
            public AmmQuote EmptyDeposit(XrplNumber amount1, XrplNumber amount2, XrplNumber tokens)
            {
                if (tokens <= XrplNumber.Zero)
                    return Refused("tecAMM_INVALID_TOKENS", "The deposit issues no LP tokens.");
                if (amount1 <= XrplNumber.Zero || amount2 <= XrplNumber.Zero)
                    return Refused("temBAD_AMOUNT", "The deposit comes out as zero of an asset.");

                return Quote(amount1, amount2, tokens, AmountMath.Add(Balance1, amount1, Kind1, Context), AmountMath.Add(Balance2, amount2, Kind2, Context), tokens);
            }

            /// <summary>A quote in the pool's own asset order.</summary>
            public AmmQuote Quote(XrplNumber first, XrplNumber second, XrplNumber tokens, XrplNumber pool1, XrplNumber pool2, XrplNumber lpTokensAfter) => new AmmQuote
            {
                Asset1 = LoanSchedule.ToDecimal(_swapped ? second : first),
                Asset2 = LoanSchedule.ToDecimal(_swapped ? first : second),
                LpTokens = LoanSchedule.ToDecimal(tokens),
                PoolAfter = new AmmPool
                {
                    Asset = _original.Asset,
                    Asset2 = _original.Asset2,
                    Balance = _swapped ? pool2 : pool1,
                    Balance2 = _swapped ? pool1 : pool2,
                    LpTokenBalance = lpTokensAfter,
                    TradingFee = _original.TradingFee,
                },
            };

            private static AmountKind KindOf(IssuedCurrency asset) =>
                asset.MptIssuanceId != null ? AmountKind.Mpt
                : asset.IsXrp() ? AmountKind.Xrp
                : AmountKind.Iou;

            private static bool SameAsset(IssuedCurrency a, IssuedCurrency b)
            {
                if (a.MptIssuanceId != null || b.MptIssuanceId != null)
                    return string.Equals(a.MptIssuanceId, b.MptIssuanceId, StringComparison.OrdinalIgnoreCase);

                return a.IsXrp() ? b.IsXrp() : string.Equals(a.Currency, b.Currency, StringComparison.Ordinal)
                    && string.Equals(a.Issuer, b.Issuer, StringComparison.Ordinal);
            }
        }
    }
}
