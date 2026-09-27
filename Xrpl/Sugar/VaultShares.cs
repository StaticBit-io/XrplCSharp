using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What a vault deposit or withdrawal would move, or why the node would refuse it: the result
    /// of <see cref="VaultShares"/>. Assets are in the vault asset's own unit - drops for XRP -
    /// and shares in whole units of the vault's share MPT.
    /// </summary>
    public sealed class VaultQuote
    {
        /// <summary>Shares minted by a deposit or burned by a withdrawal.</summary>
        public decimal Shares { get; init; }

        /// <summary>Assets that move into the vault on a deposit, or out of it on a withdrawal.</summary>
        public decimal Assets { get; init; }

        /// <summary>
        /// The result the node would give instead, such as <c>tecPRECISION_LOSS</c>,
        /// <c>tecINSUFFICIENT_FUNDS</c>, <c>tecLIMIT_EXCEEDED</c> or <c>tecPATH_DRY</c> (the
        /// conversion overflows); null when these checks pass.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when these checks pass.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the transaction passes the checks <see cref="VaultShares"/> repeats.</summary>
        public bool IsAccepted => Refusal == null;
    }

    /// <summary>
    /// Vault deposits and withdrawals computed offline from the <c>Vault</c> entry and its share
    /// issuance, the way rippled's <c>VaultDeposit</c>, <c>VaultWithdraw</c> and
    /// <c>VaultHelpers.cpp</c> compute them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeated here is everything that depends on the amounts: the conversion between assets and
    /// shares, the rounding to the vault's scale, and the refusals it leads to - a deposit or
    /// withdrawal too small to move a share or a representable amount (<c>tecPRECISION_LOSS</c>),
    /// the vault's available assets or the holder's shares (<c>tecINSUFFICIENT_FUNDS</c>), and
    /// <c>AssetsMaximum</c> (<c>tecLIMIT_EXCEEDED</c>). Authorization, freezes, the vault's phase
    /// and the account's own funds are not; <see cref="PreviewSugar.PreviewVaultDeposit"/> and
    /// <see cref="PreviewSugar.PreviewVaultWithdraw"/> cover them.
    /// </para>
    /// <para>
    /// The share count comes from the share issuance: <c>OutstandingAmount</c> of the
    /// <c>MPTokenIssuance</c> whose ID is the vault's <c>ShareMPTID</c>.
    /// </para>
    /// </remarks>
    public static class VaultShares
    {
        /// <summary>
        /// A <c>VaultDeposit</c> of <paramref name="amount"/>: the shares minted and the assets
        /// the vault takes, which can be less than the amount.
        /// </summary>
        /// <param name="vault">The <c>Vault</c> entry.</param>
        /// <param name="sharesOutstanding">The share issuance's <c>OutstandingAmount</c>.</param>
        /// <param name="amount">The deposit, in the vault asset's unit.</param>
        /// <param name="depositorBalance">
        /// The depositor's balance of an issued-currency asset, for the check that the deposit
        /// changes it at all; null skips that check. Not used for XRP and MPT.
        /// </param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in, for the phases of a
        /// closed-ended vault; null skips that check. A local time is converted to UTC; an
        /// unspecified one is taken as UTC.
        /// </param>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static VaultQuote Deposit(
            LOVault vault,
            ulong sharesOutstanding,
            XrplNumber amount,
            XrplNumber? depositorBalance = null,
            LedgerRules rules = null,
            DateTime? parentCloseTime = null)
        {
            VaultState state = VaultState.Of(vault, sharesOutstanding, rules);
            return Guarded(() => DepositCore(state, vault, amount, depositorBalance, parentCloseTime));
        }

        /// <summary>
        /// A <c>VaultWithdraw</c> of <paramref name="amount"/> in the vault asset: the shares burned
        /// and the assets paid out.
        /// </summary>
        /// <param name="vault">The <c>Vault</c> entry.</param>
        /// <param name="sharesOutstanding">The share issuance's <c>OutstandingAmount</c>.</param>
        /// <param name="holderShares">The withdrawing account's shares.</param>
        /// <param name="amount">The assets to withdraw.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in, for the phases of a
        /// closed-ended vault; null skips that check. A local time is converted to UTC; an
        /// unspecified one is taken as UTC.
        /// </param>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static VaultQuote WithdrawAssets(
            LOVault vault,
            ulong sharesOutstanding,
            ulong holderShares,
            XrplNumber amount,
            LedgerRules rules = null,
            DateTime? parentCloseTime = null)
        {
            VaultState state = VaultState.Of(vault, sharesOutstanding, rules);
            return Guarded(() => WithdrawAssetsCore(state, vault, holderShares, amount, parentCloseTime));
        }

        /// <summary>
        /// A <c>VaultWithdraw</c> of <paramref name="shares"/> vault shares: the assets they pay out.
        /// </summary>
        /// <param name="vault">The <c>Vault</c> entry.</param>
        /// <param name="sharesOutstanding">The share issuance's <c>OutstandingAmount</c>.</param>
        /// <param name="holderShares">The withdrawing account's shares.</param>
        /// <param name="shares">The shares to redeem.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="parentCloseTime">
        /// The close time of the ledger before the one the transaction lands in, for the phases of a
        /// closed-ended vault; null skips that check. A local time is converted to UTC; an
        /// unspecified one is taken as UTC.
        /// </param>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static VaultQuote RedeemShares(
            LOVault vault,
            ulong sharesOutstanding,
            ulong holderShares,
            ulong shares,
            LedgerRules rules = null,
            DateTime? parentCloseTime = null)
        {
            VaultState state = VaultState.Of(vault, sharesOutstanding, rules);
            return Guarded(() => RedeemSharesCore(state, vault, holderShares, shares, parentCloseTime));
        }

        /// <summary>
        /// Runs a computation the way <c>VaultDeposit</c> and <c>VaultWithdraw</c> run theirs: an
        /// overflow in the <c>Number</c> arithmetic, a division by zero included, is the node's
        /// <c>tecPATH_DRY</c>. The result becomes a <see cref="decimal"/> outside the guard, so an
        /// amount beyond it still throws.
        /// </summary>
        private static VaultQuote Guarded(Func<Outcome> compute)
        {
            Outcome outcome;
            try
            {
                outcome = compute();
            }
            catch (ArithmeticException)
            {
                return new VaultQuote { Refusal = "tecPATH_DRY", RefusalReason = "The share and asset conversion overflows." };
            }

            return outcome.Refusal != null
                ? new VaultQuote { Refusal = outcome.Refusal, RefusalReason = outcome.Reason }
                : new VaultQuote { Shares = LoanSchedule.ToDecimal(outcome.Shares), Assets = LoanSchedule.ToDecimal(outcome.Assets) };
        }

        /// <summary><c>VaultDeposit::doApply</c>.</summary>
        private static Outcome DepositCore(VaultState state, LOVault vault, XrplNumber amount, XrplNumber? depositorBalance, DateTime? parentCloseTime)
        {
            NumberContext c = state.Context;
            if (amount <= XrplNumber.Zero)
                return Refused("temBAD_AMOUNT", "The amount is not positive.");

            if (state.Rules.LendingProtocolV1_1 && PhaseAt(vault, parentCloseTime) is VaultPhase.Investment or VaultPhase.Redemption)
                return Refused("tecEXPIRED", "A closed-ended vault takes deposits only in its subscription phase.");

            XrplNumber assets = AssetRounding.ToAmount(amount, state.Integral, c);
            if (state.Rules.FixCleanup3_2_0)
                assets = RoundToVaultScale(assets, state);
            if (assets.IsZero)
                return Refused("tecINTERNAL", "The amount rounds to zero at the vault's scale.");

            XrplNumber shares = AssetsToSharesDeposit(assets, state);
            if (shares.IsZero)
                return Refused("tecPRECISION_LOSS", "The deposit is worth less than one share.");

            XrplNumber deposited = SharesToAssetsDeposit(shares, state);
            if (deposited > assets)
                return Refused("tecINTERNAL", "The shares are worth more than the deposit.");

            if (state.Rules.FixCleanup3_4_0)
            {
                XrplNumber? clamped = ClampToAssetsTotalScale(deposited, isWithdrawal: false, state);
                if (clamped is not { } credited)
                    return Refused("tecPRECISION_LOSS", "The deposit is below the precision of the vault's total.");

                deposited = credited;
                if (!state.Integral && depositorBalance is { } balance
                    && AssetRounding.ToAmount(XrplNumber.Subtract(balance, deposited, c), false, c) == balance)
                {
                    return Refused("tecPRECISION_LOSS", "The deposit is too small to change the depositor's balance.");
                }
            }

            XrplNumber maximum = vault.AssetsMaximum ?? XrplNumber.Zero;
            if (!maximum.IsZero && XrplNumber.Add(state.AssetsTotal, deposited, c) > maximum)
                return Refused("tecLIMIT_EXCEEDED", "The deposit would take the vault's assets past AssetsMaximum.");

            return Quote(shares, deposited);
        }

        /// <summary><c>VaultWithdraw::doApply</c> for an amount of the asset.</summary>
        private static Outcome WithdrawAssetsCore(VaultState state, LOVault vault, ulong holderShares, XrplNumber amount, DateTime? parentCloseTime)
        {
            if (amount <= XrplNumber.Zero)
                return Refused("temBAD_AMOUNT", "The amount is not positive.");

            if (InvestmentPhaseRefusal(state, vault, parentCloseTime) is { } phase)
                return phase;

            bool waive = WaivesLoss(state, holderShares);
            XrplNumber shares = AssetsToSharesWithdraw(AssetRounding.ToAmount(amount, state.Integral, state.Context), state, waive);
            if (shares.IsZero)
                return Refused("tecPRECISION_LOSS", "The withdrawal is worth less than one share.");

            return Withdraw(shares, SharesToAssetsWithdraw(shares, state, waive), byShares: false, holderShares, waive, state);
        }

        /// <summary><c>VaultWithdraw::doApply</c> for a number of shares.</summary>
        private static Outcome RedeemSharesCore(VaultState state, LOVault vault, ulong holderShares, ulong shares, DateTime? parentCloseTime)
        {
            if (shares == 0)
                return Refused("temBAD_AMOUNT", "The amount is not positive.");

            if (InvestmentPhaseRefusal(state, vault, parentCloseTime) is { } phase)
                return phase;

            bool waive = WaivesLoss(state, holderShares);
            XrplNumber redeemed = FromULong(shares);
            return Withdraw(redeemed, SharesToAssetsWithdraw(redeemed, state, waive), byShares: true, holderShares, waive, state);
        }

        private static Outcome InvestmentPhaseRefusal(VaultState state, LOVault vault, DateTime? parentCloseTime) =>
            state.Rules.LendingProtocolV1_1 && PhaseAt(vault, parentCloseTime) == VaultPhase.Investment
                ? Refused("tecTOO_SOON", "A closed-ended vault pays out nothing in its investment phase.")
                : null;

        /// <summary>
        /// <c>getVaultPhase</c>: subscription up to and including <c>SubscriptionDate</c>, investment
        /// until <c>RedemptionDate</c>, redemption from it on. <see cref="VaultPhase.None"/> for an
        /// open-ended vault, or when no time is given.
        /// </summary>
        private static VaultPhase PhaseAt(LOVault vault, DateTime? parentCloseTime)
        {
            if (parentCloseTime is not { } time || vault.VaultKind != (uint)VaultKind.ClosedEnded)
                return VaultPhase.None;

            DateTime now = time.Kind switch
            {
                DateTimeKind.Local => time.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(time, DateTimeKind.Utc),
                _ => time,
            };
            if (vault.SubscriptionDate is not { } subscription || now <= subscription)
                return VaultPhase.Subscription;
            if (vault.RedemptionDate is not { } redemption || now < redemption)
                return VaultPhase.Investment;
            return VaultPhase.Redemption;
        }

        private enum VaultPhase
        {
            None,
            Subscription,
            Investment,
            Redemption,
        }

        /// <summary>The checks <c>VaultWithdraw::doApply</c> makes once shares and assets are known.</summary>
        private static Outcome Withdraw(XrplNumber shares, XrplNumber assets, bool byShares, ulong holderShares, bool waive, VaultState state)
        {
            NumberContext c = state.Context;
            bool final = shares == FromULong(state.SharesOutstanding);

            if (state.Rules.FixCleanup3_4_0 && !final)
            {
                if (byShares && assets.IsZero && !AssetsTotalForWithdrawal(state, waive).IsZero)
                    return Refused("tecPRECISION_LOSS", "The shares are worth less than the asset's smallest amount.");
                if (!assets.IsZero
                    && AssetRounding.ToAmount(XrplNumber.Subtract(state.AssetsTotal, assets, c), state.Integral, c)
                        == AssetRounding.ToAmount(state.AssetsTotal, state.Integral, c))
                {
                    return Refused("tecPRECISION_LOSS", "The withdrawal is too small to change the vault's total.");
                }
            }

            if (FromULong(holderShares) < shares)
                return Refused("tecINSUFFICIENT_FUNDS", "The account holds fewer shares than the withdrawal burns.");

            if (state.Rules.FixCleanup3_4_0 && !final && assets > XrplNumber.Zero)
            {
                if (state.AssetsAvailable < assets)
                    return Refused("tecINSUFFICIENT_FUNDS", "The vault has fewer assets available than the withdrawal.");

                XrplNumber? clamped = ClampToAssetsTotalScale(assets, isWithdrawal: true, state);
                if (clamped is not { } paid)
                    return Refused("tecPRECISION_LOSS", "The withdrawal is below the precision of the vault's total.");

                assets = paid;
            }

            if (state.AssetsAvailable < assets)
                return Refused("tecINSUFFICIENT_FUNDS", "The vault has fewer assets available than the withdrawal.");

            if (state.Rules.FixCleanup3_2_0 && final)
                assets = AssetRounding.ToAmount(state.AssetsAvailable, state.Integral, c);

            return Quote(shares, assets);
        }

        /// <summary><c>assetsToSharesDeposit</c>: shares minted, truncated to a whole share.</summary>
        private static XrplNumber AssetsToSharesDeposit(XrplNumber assets, VaultState state)
        {
            NumberContext c = state.Context;
            XrplNumber shares = state.AssetsTotal.IsZero
                ? new XrplNumber(assets.Mantissa, checked(assets.Exponent + state.Scale)).Truncate(c)
                : XrplNumber.Divide(XrplNumber.Multiply(FromULong(state.SharesOutstanding), assets, c), state.AssetsTotal, c).Truncate(c);
            return AssetRounding.ToAmount(shares, integral: true, c);
        }

        /// <summary><c>sharesToAssetsDeposit</c>: what the minted shares are worth in the asset.</summary>
        private static XrplNumber SharesToAssetsDeposit(XrplNumber shares, VaultState state)
        {
            NumberContext c = state.Context;
            if (state.AssetsTotal.IsZero)
            {
                // STAmount{asset, shares, -Scale}: an integral amount with an offset at or below -20 is zero.
                if (state.Integral && state.Scale >= 20)
                    return XrplNumber.Zero;

                return AssetRounding.ToAmount(new XrplNumber(shares.Mantissa, checked(shares.Exponent - state.Scale)), state.Integral, c);
            }

            return AssetRounding.ToAmount(
                XrplNumber.Divide(XrplNumber.Multiply(state.AssetsTotal, shares, c), FromULong(state.SharesOutstanding), c),
                state.Integral,
                c);
        }

        /// <summary><c>assetsToSharesWithdraw</c>: shares burned, truncated under <c>fixCleanup3_4_0</c>.</summary>
        private static XrplNumber AssetsToSharesWithdraw(XrplNumber assets, VaultState state, bool waive)
        {
            NumberContext c = state.Context;
            XrplNumber total = AssetsTotalForWithdrawal(state, waive);
            if (total.IsZero)
                return XrplNumber.Zero;

            XrplNumber result = XrplNumber.Divide(XrplNumber.Multiply(FromULong(state.SharesOutstanding), assets, c), total, c);
            if (state.Rules.FixCleanup3_4_0)
                result = result.Truncate(c);

            return AssetRounding.ToAmount(result, integral: true, c);
        }

        /// <summary><c>sharesToAssetsWithdraw</c>: what the burned shares pay out.</summary>
        private static XrplNumber SharesToAssetsWithdraw(XrplNumber shares, VaultState state, bool waive)
        {
            NumberContext c = state.Context;
            XrplNumber total = AssetsTotalForWithdrawal(state, waive);
            if (total.IsZero)
                return XrplNumber.Zero;

            return AssetRounding.ToAmount(
                XrplNumber.Divide(XrplNumber.Multiply(total, shares, c), FromULong(state.SharesOutstanding), c),
                state.Integral,
                c);
        }

        /// <summary><c>assetsTotalForWithdrawal</c>: the total, less the unrealized loss unless it is waived.</summary>
        private static XrplNumber AssetsTotalForWithdrawal(VaultState state, bool waive) =>
            waive ? state.AssetsTotal : XrplNumber.Subtract(state.AssetsTotal, state.LossUnrealized, state.Context);

        /// <summary><c>shouldWaiveWithdrawal</c>: the sole shareholder bears no unrealized loss, under <c>fixCleanup3_2_0</c>.</summary>
        private static bool WaivesLoss(VaultState state, ulong holderShares) =>
            state.Rules.FixCleanup3_2_0 && state.SharesOutstanding != 0 && holderShares == state.SharesOutstanding;

        /// <summary>
        /// <c>roundToVaultScale</c>: an issued-currency deposit rounded down to the scale the vault's
        /// total will have after it.
        /// </summary>
        private static XrplNumber RoundToVaultScale(XrplNumber amount, VaultState state)
        {
            if (state.Integral)
                return amount;

            NumberContext c = state.Context;
            int postScale = AssetRounding.Scale(XrplNumber.Add(state.AssetsTotal, amount, c), false, c);
            return AssetRounding.Round(amount, false, postScale, c.WithRounding(NumberRounding.Downward));
        }

        /// <summary>
        /// <c>clampToAssetsTotalScale</c>: the change aligned with the scale of the vault's total
        /// after it, never larger than asked; null when nothing representable is left.
        /// </summary>
        private static XrplNumber? ClampToAssetsTotalScale(XrplNumber magnitude, bool isWithdrawal, VaultState state)
        {
            if (state.Integral)
                return magnitude;

            NumberContext c = state.Context;
            NumberContext down = c.WithRounding(NumberRounding.Downward);
            XrplNumber delta = isWithdrawal ? -magnitude : magnitude;
            int postScale = AssetRounding.Scale(XrplNumber.Add(state.AssetsTotal, delta, c), false, c);

            XrplNumber actual;
            if (isWithdrawal)
            {
                actual = AssetRounding.Round(magnitude, false, postScale, down);
            }
            else
            {
                XrplNumber posterior = XrplNumber.Add(state.AssetsTotal, magnitude, down);
                XrplNumber rounded = AssetRounding.Round(posterior, false, postScale, down);
                actual = AssetRounding.ToAmount(XrplNumber.Subtract(rounded, state.AssetsTotal, c), false, c);
            }

            return actual <= XrplNumber.Zero ? null : actual;
        }

        /// <summary>An MPT amount as a number; MPT amounts never exceed <see cref="long.MaxValue"/>.</summary>
        private static XrplNumber FromULong(ulong value) => (XrplNumber)checked((long)value);

        private static Outcome Quote(XrplNumber shares, XrplNumber assets) => new Outcome(shares, assets, null, null);

        private static Outcome Refused(string result, string reason) => new Outcome(XrplNumber.Zero, XrplNumber.Zero, result, reason);

        /// <summary>A computation's result before it becomes a <see cref="VaultQuote"/>.</summary>
        private sealed record Outcome(XrplNumber Shares, XrplNumber Assets, string Refusal, string Reason);

        /// <summary>The vault fields the computations read.</summary>
        private sealed class VaultState
        {
            public LedgerRules Rules { get; private init; }

            public NumberContext Context { get; private init; }

            public bool Integral { get; private init; }

            public int Scale { get; private init; }

            public XrplNumber AssetsTotal { get; private init; }

            public XrplNumber AssetsAvailable { get; private init; }

            public XrplNumber LossUnrealized { get; private init; }

            public ulong SharesOutstanding { get; private init; }

            public static VaultState Of(LOVault vault, ulong sharesOutstanding, LedgerRules rules)
            {
                if (vault == null)
                    throw new ArgumentNullException(nameof(vault));
                if (vault.Asset is not IssuedCurrency asset)
                    throw new ArgumentException("The vault carries no Asset.", nameof(vault));

                rules ??= new LedgerRules();
                return new VaultState
                {
                    Rules = rules,
                    Context = rules.Context,
                    Integral = LoanPayments.IsIntegral(asset),
                    Scale = (int)(vault.Scale ?? 0),
                    AssetsTotal = vault.AssetsTotal ?? XrplNumber.Zero,
                    AssetsAvailable = vault.AssetsAvailable ?? XrplNumber.Zero,
                    LossUnrealized = vault.LossUnrealized ?? XrplNumber.Zero,
                    SharesOutstanding = sharesOutstanding,
                };
            }
        }
    }
}
