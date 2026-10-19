using System;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Ledger;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What a change to a loan broker's first-loss cover would do, or why the node would refuse it:
    /// the result of <see cref="LoanBrokerCover"/>. Amounts are in the vault asset's own unit - drops
    /// for XRP - as the <c>LoanBroker</c> fields are.
    /// </summary>
    public sealed class LoanBrokerCoverQuote
    {
        /// <summary>
        /// What moves: the amount a deposit credits to the cover once rounded to its scale, the amount
        /// a withdrawal takes out, or what a clawback takes back. Zero when refused.
        /// </summary>
        public decimal Amount { get; init; }

        /// <summary>The broker's <c>CoverAvailable</c> before the transaction.</summary>
        public decimal CoverBefore { get; init; }

        /// <summary>The broker's <c>CoverAvailable</c> after the transaction; <see cref="CoverBefore"/> when refused.</summary>
        public decimal CoverAfter { get; init; }

        /// <summary>
        /// The minimum cover the transaction checks against: the one <c>LoanBrokerCoverClawback</c>
        /// keeps for a clawback, the one <c>LoanBrokerCoverWithdraw</c> keeps otherwise.
        /// </summary>
        public decimal MinimumCover { get; init; }

        /// <summary>
        /// The result the node would give instead, such as <c>tecINSUFFICIENT_FUNDS</c> or
        /// <c>tecPRECISION_LOSS</c>; null when these checks pass.
        /// </summary>
        public string Refusal { get; init; }

        /// <summary>Why the node would refuse, in words; null when these checks pass.</summary>
        public string RefusalReason { get; init; }

        /// <summary>Whether the transaction passes the checks <see cref="LoanBrokerCover"/> repeats.</summary>
        public bool IsAccepted => Refusal == null;
    }

    /// <summary>
    /// A loan broker's first-loss cover computed offline from the <c>LoanBroker</c> and <c>Vault</c>
    /// entries, the way rippled's <c>LoanBrokerCoverDeposit</c>, <c>LoanBrokerCoverWithdraw</c>,
    /// <c>LoanBrokerCoverClawback</c> and <c>minimumBrokerCover</c> compute it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The broker must keep <c>CoverRateMinimum</c> (in 1/10 basis points) of its <c>DebtTotal</c>
    /// as cover, rounded up. Under <c>fixCleanup3_2_0</c> the minimum is rounded to the scale of the
    /// vault's <c>AssetsTotal</c>. Before it, a withdrawal rounds it to the scale of
    /// <c>DebtTotal</c> and a clawback does not round it to the asset at all, so the same broker
    /// can have a different minimum on either side of the amendment.
    /// </para>
    /// <para>
    /// Repeated here is everything that depends on the amounts: the minimum, the rounding of a
    /// deposit to the cover's scale and the refusals they lead to - an amount below that scale
    /// (<c>tecPRECISION_LOSS</c>) and a withdrawal or clawback that would take the cover below the
    /// minimum (<c>tecINSUFFICIENT_FUNDS</c>). Ownership, the asset, transferability, freezes,
    /// authorization, the destination, the depositor's funds, what the broker's pseudo-account
    /// actually holds and the issuer's clawback flags are not;
    /// <see cref="PreviewSugar.PreviewLoanBrokerCoverDeposit"/>,
    /// <see cref="PreviewSugar.PreviewLoanBrokerCoverWithdraw"/> and
    /// <see cref="PreviewSugar.PreviewLoanBrokerCoverClawback"/> cover them.
    /// </para>
    /// </remarks>
    public static class LoanBrokerCover
    {
        /// <summary>
        /// The minimum cover <c>LoanBrokerCoverWithdraw</c> requires the broker to keep.
        /// </summary>
        /// <remarks>
        /// <c>LoanPay</c> compares the cover with the same minimum under <c>fixCleanup3_2_0</c> to
        /// decide whether the broker's fee goes to its owner. Before the amendment it rounds at the
        /// loan's scale instead, which this method does not compute.
        /// </remarks>
        /// <param name="broker">The <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or is not the broker's.</exception>
        /// <exception cref="OverflowException">The minimum is beyond <see cref="decimal"/>.</exception>
        public static decimal MinimumCover(LOLoanBroker broker, LOVault vault, LedgerRules rules = null)
        {
            CoverState state = CoverState.Of(broker, vault, rules);
            return LoanSchedule.ToDecimal(WithdrawMinimum(state));
        }

        /// <summary>
        /// The largest <c>LoanBrokerCoverWithdraw</c> the node accepts: the cover above the minimum,
        /// as an amount of the asset rounded down. Zero when nothing can be withdrawn.
        /// </summary>
        /// <param name="broker">The <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or is not the broker's.</exception>
        /// <exception cref="OverflowException">The amount is beyond <see cref="decimal"/>.</exception>
        public static decimal MaxWithdrawable(LOLoanBroker broker, LOVault vault, LedgerRules rules = null)
        {
            CoverState state = CoverState.Of(broker, vault, rules);
            return LoanSchedule.ToDecimal(MaxWithdrawableCore(state, WithdrawMinimum(state)));
        }

        /// <summary>
        /// A <c>LoanBrokerCoverDeposit</c> of <paramref name="amount"/>: what the cover is credited,
        /// which under <c>fixCleanup3_2_0</c> is the amount rounded down to the cover's scale.
        /// </summary>
        /// <param name="broker">The <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="amount">The deposit, in the vault asset's unit.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or is not the broker's.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanBrokerCoverQuote CheckDeposit(LOLoanBroker broker, LOVault vault, XrplNumber amount, LedgerRules rules = null)
        {
            CoverState state = CoverState.Of(broker, vault, rules);
            NumberContext c = state.Context;
            XrplNumber minimum = WithdrawMinimum(state);

            XrplNumber credited = AssetRounding.ToAmount(amount, state.Integral, c);
            if (credited <= XrplNumber.Zero)
                return Refused(state, minimum, "temBAD_AMOUNT", "The amount is not positive.");

            if (state.Rules.FixCleanup3_2_0)
            {
                credited = AssetRounding.Round(credited, state.Integral, CoverScale(state), c.WithRounding(NumberRounding.Downward));
                if (credited.IsZero)
                    return Refused(state, minimum, "tecPRECISION_LOSS", "The deposit rounds to zero at the cover's scale.");
            }

            return Accepted(state, minimum, credited, XrplNumber.Add(state.Cover, credited, c));
        }

        /// <summary>
        /// A <c>LoanBrokerCoverWithdraw</c> of <paramref name="amount"/>: whether the cover left
        /// keeps the minimum.
        /// </summary>
        /// <param name="broker">The <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="amount">The withdrawal, in the vault asset's unit.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or is not the broker's.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanBrokerCoverQuote CheckWithdraw(LOLoanBroker broker, LOVault vault, XrplNumber amount, LedgerRules rules = null)
        {
            CoverState state = CoverState.Of(broker, vault, rules);
            XrplNumber minimum = WithdrawMinimum(state);

            XrplNumber requested = AssetRounding.ToAmount(amount, state.Integral, state.Context);
            if (requested <= XrplNumber.Zero)
                return Refused(state, minimum, "temBAD_AMOUNT", "The amount is not positive.");

            if (WithdrawRefusal(state, requested, minimum) is { } refusal)
                return Refused(state, minimum, refusal.Result, refusal.Reason);

            return Accepted(state, minimum, requested, XrplNumber.Subtract(state.Cover, requested, state.Context));
        }

        /// <summary>
        /// A <c>LoanBrokerCoverClawback</c> by the asset's issuer: what it takes back. Without an
        /// amount, or with zero, the issuer takes everything above the minimum; a larger amount is
        /// cut down to that.
        /// </summary>
        /// <param name="broker">The <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="amount">The amount to claw back, in the vault asset's unit; null takes all it can.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or is not the broker's.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static LoanBrokerCoverQuote CheckClawback(LOLoanBroker broker, LOVault vault, XrplNumber? amount = null, LedgerRules rules = null)
        {
            CoverState state = CoverState.Of(broker, vault, rules);
            NumberContext c = state.Context;
            XrplNumber minimum = ClawbackMinimum(state);

            if (state.Asset.IsXrp())
            {
                return amount is null
                    ? Refused(state, minimum, "tecNO_PERMISSION", "XRP has no issuer, so it cannot be clawed back.")
                    : Refused(state, minimum, "temBAD_AMOUNT", "XRP has no issuer, so it cannot be clawed back.");
            }

            XrplNumber? requested = amount is { } a ? AssetRounding.ToAmount(a, state.Integral, c) : null;
            if (requested < XrplNumber.Zero)
                return Refused(state, minimum, "temBAD_AMOUNT", "The amount is negative.");

            // determineClawAmount: the subtraction rounds down; the amount is then made in the
            // ambient mode.
            XrplNumber maximum = XrplNumber.Subtract(state.Cover, minimum, c.WithRounding(NumberRounding.Downward));
            if (maximum <= XrplNumber.Zero)
                return Refused(state, minimum, "tecINSUFFICIENT_FUNDS", "The cover is already at or below the minimum.");

            XrplNumber clawed = requested is not { } r || r.IsZero || r > maximum
                ? AssetRounding.ToAmount(maximum, state.Integral, c)
                : r;

            if (state.Rules.FixCleanup3_2_0 && PrecisionRefusal(state, clawed) is { } precision)
                return Refused(state, minimum, precision.Result, precision.Reason);

            return Accepted(state, minimum, clawed, XrplNumber.Subtract(state.Cover, clawed, c));
        }

        /// <summary>
        /// What a <c>LoanBrokerCoverClawback</c> without an amount takes back; zero when the node
        /// would refuse it.
        /// </summary>
        /// <param name="broker">The <c>LoanBroker</c> entry.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The vault carries no asset, or is not the broker's.</exception>
        /// <exception cref="OverflowException">An amount is beyond <see cref="decimal"/>.</exception>
        public static decimal MaxClawback(LOLoanBroker broker, LOVault vault, LedgerRules rules = null)
        {
            LoanBrokerCoverQuote quote = CheckClawback(broker, vault, null, rules);
            return quote.IsAccepted ? quote.Amount : 0m;
        }

        /// <summary>The withdrawal <see cref="MaxWithdrawable"/> returns, before it becomes a <see cref="decimal"/>.</summary>
        internal static XrplNumber MaxWithdrawableCore(CoverState state, XrplNumber minimum)
        {
            NumberContext down = state.Context.WithRounding(NumberRounding.Downward);
            XrplNumber headroom = XrplNumber.Subtract(state.Cover, minimum, down);
            if (headroom <= XrplNumber.Zero)
                return XrplNumber.Zero;

            XrplNumber candidate = AssetRounding.ToAmount(headroom, state.Integral, down);
            return candidate > XrplNumber.Zero && WithdrawRefusal(state, candidate, minimum) == null
                ? candidate
                : XrplNumber.Zero;
        }

        /// <summary>
        /// <c>minimumBrokerCover</c>: <c>CoverRateMinimum</c> of <c>DebtTotal</c> rounded up, to the
        /// scale of the vault's <c>AssetsTotal</c>. The scale is taken inside the same upward guard.
        /// </summary>
        private static XrplNumber MinimumBrokerCover(CoverState state)
        {
            NumberContext up = state.Context.WithRounding(NumberRounding.Upward);
            return AssetRounding.Round(
                RequiredCover(state, up),
                state.Integral,
                AssetRounding.ScaleInMode(state.AssetsTotal, state.Integral, up),
                up);
        }

        /// <summary>
        /// The minimum <c>LoanBrokerCoverWithdraw</c> keeps: <c>minimumBrokerCover</c> under
        /// <c>fixCleanup3_2_0</c>, before it the product rounded up to the scale of <c>DebtTotal</c>.
        /// </summary>
        internal static XrplNumber WithdrawMinimum(CoverState state)
        {
            if (state.Rules.FixCleanup3_2_0)
                return MinimumBrokerCover(state);

            NumberContext up = state.Context.WithRounding(NumberRounding.Upward);
            return AssetRounding.Round(
                RequiredCover(state, up),
                state.Integral,
                AssetRounding.ScaleInMode(state.DebtTotal, state.Integral, up),
                up);
        }

        /// <summary>
        /// The minimum <c>LoanBrokerCoverClawback</c> keeps: <c>minimumBrokerCover</c> under
        /// <c>fixCleanup3_2_0</c>, before it the product rounded up but not to the asset.
        /// </summary>
        private static XrplNumber ClawbackMinimum(CoverState state) =>
            state.Rules.FixCleanup3_2_0
                ? MinimumBrokerCover(state)
                : RequiredCover(state, state.Context.WithRounding(NumberRounding.Upward));

        /// <summary><c>tenthBipsOfValue(DebtTotal, CoverRateMinimum)</c>.</summary>
        private static XrplNumber RequiredCover(CoverState state, NumberContext context) =>
            LendingMath.TenthBipsOf(state.DebtTotal, state.CoverRateMinimum, context);

        /// <summary>The checks <c>LoanBrokerCoverWithdraw</c>'s preclaim makes on the amount.</summary>
        private static (string Result, string Reason)? WithdrawRefusal(CoverState state, XrplNumber requested, XrplNumber minimum)
        {
            if (state.Rules.FixCleanup3_2_0 && PrecisionRefusal(state, requested) is { } precision)
                return precision;

            if (state.Cover < requested)
                return ("tecINSUFFICIENT_FUNDS", "The withdrawal is more than the cover available.");

            if (XrplNumber.Subtract(state.Cover, requested, state.Context) < minimum)
                return ("tecINSUFFICIENT_FUNDS", "The withdrawal would leave less than the minimum cover.");

            return null;
        }

        /// <summary><c>canApplyToBrokerCover</c>: an amount that rounds to zero at the cover's scale.</summary>
        private static (string Result, string Reason)? PrecisionRefusal(CoverState state, XrplNumber amount)
        {
            NumberContext nearest = state.Context.WithRounding(NumberRounding.ToNearest);
            return amount.IsZero || AssetRounding.Round(amount, state.Integral, CoverScale(state), nearest).IsZero
                ? ("tecPRECISION_LOSS", "The amount rounds to zero at the cover's scale.")
                : null;
        }

        /// <summary><c>scale(CoverAvailable, asset)</c>, in the ambient mode.</summary>
        private static int CoverScale(CoverState state) =>
            AssetRounding.ScaleInMode(state.Cover, state.Integral, state.Context);

        private static LoanBrokerCoverQuote Accepted(CoverState state, XrplNumber minimum, XrplNumber moved, XrplNumber after) =>
            new LoanBrokerCoverQuote
            {
                Amount = LoanSchedule.ToDecimal(moved),
                CoverBefore = LoanSchedule.ToDecimal(state.Cover),
                CoverAfter = LoanSchedule.ToDecimal(after),
                MinimumCover = LoanSchedule.ToDecimal(minimum),
            };

        private static LoanBrokerCoverQuote Refused(CoverState state, XrplNumber minimum, string result, string reason)
        {
            decimal cover = LoanSchedule.ToDecimal(state.Cover);
            return new LoanBrokerCoverQuote
            {
                CoverBefore = cover,
                CoverAfter = cover,
                MinimumCover = LoanSchedule.ToDecimal(minimum),
                Refusal = result,
                RefusalReason = reason,
            };
        }

        /// <summary>The broker and vault fields the computations read.</summary>
        internal sealed class CoverState
        {
            public LedgerRules Rules { get; private init; }

            public NumberContext Context { get; private init; }

            public IssuedCurrency Asset { get; private init; }

            public bool Integral { get; private init; }

            public XrplNumber Cover { get; private init; }

            public XrplNumber DebtTotal { get; private init; }

            public uint CoverRateMinimum { get; private init; }

            public XrplNumber AssetsTotal { get; private init; }

            public static CoverState Of(LOLoanBroker broker, LOVault vault, LedgerRules rules)
            {
                if (broker == null)
                    throw new ArgumentNullException(nameof(broker));
                if (vault == null)
                    throw new ArgumentNullException(nameof(vault));
                if (vault.Asset is not IssuedCurrency asset)
                    throw new ArgumentException("The vault carries no Asset.", nameof(vault));

                string vaultId = vault.Index ?? vault.LedgerIndex;
                if (broker.VaultID != null && vaultId != null && !string.Equals(broker.VaultID, vaultId, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"The vault {vaultId} is not the broker's vault {broker.VaultID}.", nameof(vault));

                rules ??= new LedgerRules();
                return new CoverState
                {
                    Rules = rules,
                    Context = rules.Context,
                    Asset = asset,
                    Integral = LoanPayments.IsIntegral(asset),
                    Cover = broker.CoverAvailable ?? XrplNumber.Zero,
                    DebtTotal = broker.DebtTotal ?? XrplNumber.Zero,
                    CoverRateMinimum = broker.CoverRateMinimum ?? 0,
                    AssetsTotal = vault.AssetsTotal ?? XrplNumber.Zero,
                };
            }
        }
    }
}
