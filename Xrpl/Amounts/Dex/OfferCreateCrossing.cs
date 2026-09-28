using System;
using System.Collections.Generic;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// An <c>OfferCreate</c> crossed locally against a <see cref="DexSnapshot"/>, the way rippled
    /// 3.4.0's payment engine crosses it: every offer of the book in quality order, the AMM pool
    /// interleaved with them, the auto-bridge through XRP when neither side is XRP, and the
    /// result the node would return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The crossing repeats <c>OfferCreate::flowCross</c> and the engine under it - strands,
    /// book steps, the AMM's synthetic offers and <c>StrandFlow</c> - including their rounding, so
    /// against the same state the amounts match the node's to the last digit.
    /// </para>
    /// <para>
    /// Not covered: permissioned-DEX domains, MPT books, <c>RequireAuth</c> on the assets, the
    /// transaction's <c>Expiration</c> and <c>OfferSequence</c>, and sponsored reserves. A result
    /// is exact only against the state it was given; <c>simulate</c> remains the reference for a
    /// transaction that is about to be submitted.
    /// </para>
    /// </remarks>
    public static class OfferCreateCrossing
    {
        private const int MaxTickSize = 16;

        /// <summary>
        /// What an <c>OfferCreate</c> by <paramref name="account"/> would do against
        /// <paramref name="snapshot"/>.
        /// </summary>
        /// <param name="snapshot">The ledger state to cross against.</param>
        /// <param name="account">The account placing the offer; it must be in the snapshot.</param>
        /// <param name="takerPays">The offer's <c>TakerPays</c>: what the account wants.</param>
        /// <param name="takerGets">The offer's <c>TakerGets</c>: what the account gives.</param>
        /// <param name="fee">The transaction's fee in drops, charged before anything is crossed.</param>
        /// <param name="flags">The offer's flags: <c>tfPassive</c>, <c>tfImmediateOrCancel</c>, <c>tfFillOrKill</c>, <c>tfSell</c>.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The account is not in the snapshot, or the two sides are the same asset.</exception>
        /// <exception cref="NotSupportedException">A side is an MPT.</exception>
        public static OfferCrossingResult Cross(
            DexSnapshot snapshot,
            string account,
            XrplAmount takerPays,
            XrplAmount takerGets,
            ulong fee,
            OfferCreateFlags flags = 0,
            LedgerRules rules = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            if (takerPays.Kind == AmountKind.Mpt || takerGets.Kind == AmountKind.Mpt)
                throw new NotSupportedException("MPT offers are not supported.");
            if (XrplAmount.SameAsset(takerPays.Asset, takerGets.Asset))
                throw new ArgumentException("An offer cannot trade an asset for itself.");

            rules ??= new LedgerRules();
            DexWorld world = new DexWorld(snapshot, account, fee);
            if (!world.Accounts.TryGetValue(account, out DexAccount creator))
                throw new ArgumentException("The account is not in the snapshot.", nameof(account));

            DexView ledger = new DexView(world, rules);
            DexView cancel = new DexView(world, rules);

            bool sell = flags.HasFlag(OfferCreateFlags.tfSell);
            if (!RoundToTickSize(world, ref takerPays, ref takerGets, sell, rules))
                return Result(world, ledger, "tesSUCCESS", takerPays, takerGets, null);

            // Crossing, the account is the taker: it pays in what the offer gives.
            (XrplAmount In, XrplAmount Out) takerAmount = (takerGets, takerPays);
            DexView flowView = new DexView(ledger);
            DexView flowCancel = new DexView(cancel);
            (string result, (XrplAmount In, XrplAmount Out) placeOffer, FlowResult flow) =
                FlowCross(flowView, flowCancel, account, takerAmount, flags, rules);
            flowView.ApplyTo(ledger);
            flowCancel.ApplyTo(cancel);

            XrplAmount paid = flow?.Succeeded == true ? flow.In : XrplAmount.Zero(takerGets.Asset);
            XrplAmount got = flow?.Succeeded == true ? flow.Out : XrplAmount.Zero(takerPays.Asset);
            if (result != "tesSUCCESS")
                return Result(world, ledger, result, paid, got, null);

            bool crossed = placeOffer != takerAmount;
            if (placeOffer.In.IsZero || placeOffer.Out.IsZero)
                return Result(world, ledger, result, paid, got, null);

            if (flags.HasFlag(OfferCreateFlags.tfFillOrKill))
                return Result(world, cancel, "tecKILLED", XrplAmount.Zero(takerGets.Asset), XrplAmount.Zero(takerPays.Asset), null);

            if (flags.HasFlag(OfferCreateFlags.tfImmediateOrCancel))
            {
                return crossed
                    ? Result(world, ledger, "tesSUCCESS", paid, got, null)
                    : Result(world, cancel, "tecKILLED", XrplAmount.Zero(takerGets.Asset), XrplAmount.Zero(takerPays.Asset), null);
            }

            // The reserve for one more object, against the balance before the fee.
            decimal reserve = snapshot.ReserveBase + (decimal)snapshot.ReserveIncrement * (ledger.OwnerCount(account) + 1);
            if (creator.Balance < reserve)
                return Result(world, ledger, crossed ? "tesSUCCESS" : "tecINSUF_RESERVE_OFFER", paid, got, null);

            return Result(world, ledger, "tesSUCCESS", paid, got, (placeOffer.Out, placeOffer.In));
        }

        /// <summary>
        /// The tick-size rounding of <c>applyGuts</c>: the side that is not exact moves so the
        /// offer's quality has at most the issuers' tick size in significant digits. False when the
        /// offer rounds to nothing.
        /// </summary>
        private static bool RoundToTickSize(DexWorld world, ref XrplAmount takerPays, ref XrplAmount takerGets, bool sell, LedgerRules rules)
        {
            int tickSize = MaxTickSize;
            if (!takerPays.IsIntegral && world.Accounts.TryGetValue(takerPays.Asset.Issuer, out DexAccount paysIssuer) && paysIssuer.TickSize != 0)
                tickSize = Math.Min(tickSize, paysIssuer.TickSize);
            if (!takerGets.IsIntegral && world.Accounts.TryGetValue(takerGets.Asset.Issuer, out DexAccount getsIssuer) && getsIssuer.TickSize != 0)
                tickSize = Math.Min(tickSize, getsIssuer.TickSize);

            if (tickSize >= MaxTickSize)
                return true;

            XrplAmount rate = Round(XrplQuality.FromAmounts(takerPays, takerGets, rules), tickSize).RateAmount;
            if (sell)
                takerPays = XrplAmountMath.Multiply(takerGets, rate, takerPays.Asset, rules);
            else
                takerGets = XrplAmountMath.Divide(takerPays, rate, takerGets.Asset, rules);

            return !takerPays.IsZero && !takerGets.IsZero;
        }

        /// <summary><c>Quality::round</c>: the mantissa rounded up to <paramref name="digits"/> significant digits.</summary>
        private static XrplQuality Round(XrplQuality quality, int digits)
        {
            ulong modulus = 1;
            for (int i = digits; i < MaxTickSize; i++)
                modulus *= 10;

            ulong exponent = quality.Value >> 56;
            ulong mantissa = quality.Value & 0x00ffffffffffffffUL;
            mantissa += modulus - 1;
            mantissa -= mantissa % modulus;
            return new XrplQuality((exponent << 56) | mantissa);
        }

        /// <summary><c>OfferCreate::flowCross</c>: the crossing, and what is left of the offer after it.</summary>
        private static (string Result, (XrplAmount In, XrplAmount Out) PlaceOffer, FlowResult Flow) FlowCross(
            DexView psb,
            DexView psbCancel,
            string account,
            (XrplAmount In, XrplAmount Out) takerAmount,
            OfferCreateFlags flags,
            LedgerRules rules)
        {
            try
            {
                XrplAmount inStartBalance = psb.AccountFunds(account, takerAmount.In);
                if (StepMath.IsNotPositive(inStartBalance))
                    return ("tecUNFUNDED_OFFER", takerAmount, null);

                uint gatewayRate = OfferCrossing.QualityOne;
                XrplAmount sendMax = takerAmount.In;
                if (takerAmount.In.Kind != AmountKind.Xrp && !string.Equals(account, takerAmount.In.Asset.Issuer, StringComparison.Ordinal))
                {
                    gatewayRate = psb.World.TransferRate(takerAmount.In.Asset.Issuer);
                    if (gatewayRate != OfferCrossing.QualityOne)
                        sendMax = XrplAmountMath.MulRound(takerAmount.In, StepMath.RateAmount(gatewayRate), takerAmount.In.Asset, roundUp: true, rules);
                }

                // The flow compares qualities with the transfer fee included, so the threshold does too.
                XrplQuality threshold = XrplQuality.FromAmounts(sendMax, takerAmount.Out, rules);
                if (flags.HasFlag(OfferCreateFlags.tfPassive))
                    threshold = new XrplQuality(threshold.Value - 1);

                if (sendMax > inStartBalance)
                    sendMax = inStartBalance;

                bool sell = flags.HasFlag(OfferCreateFlags.tfSell);
                XrplAmount deliver = takerAmount.Out;
                if (sell)
                {
                    // Selling, the account takes as much as it can get, so the output is unbounded.
                    deliver = deliver.Kind == AmountKind.Xrp
                        ? XrplAmount.FromUnits(deliver.Asset, AmountKind.Xrp, 9_000_000_000_000_000_000)
                        : XrplAmount.Canonical(deliver.Asset, AmountKind.Iou, false, XrplAmount.MaxIouMantissa / 2, XrplAmount.MaxIouExponent, NumberRounding.ToNearest);
                }

                AmmFlowContext ammContext = new AmmFlowContext(account);
                List<List<FlowStep>> strands = BuildStrands(psb, account, takerAmount.In.Asset, takerAmount.Out.Asset, threshold, ammContext);
                ammContext.MultiPath = strands.Count > 1;

                FlowResult flow = StrandFlow.Run(
                    psb,
                    strands,
                    takerAmount.In.Asset,
                    deliver,
                    partialPayment: !flags.HasFlag(OfferCreateFlags.tfFillOrKill),
                    sell ? CrossingMode.Sell : CrossingMode.Yes,
                    threshold,
                    sendMax,
                    ammContext);

                if (flow.Succeeded)
                    flow.Sandbox.ApplyTo(psb);

                foreach (string index in flow.RemovableOffers)
                {
                    if (!psb.Offer(index).Deleted)
                        psb.DeleteOffer(index);
                    if (!psbCancel.Offer(index).Deleted)
                        psbCancel.DeleteOffer(index);
                }

                (XrplAmount In, XrplAmount Out) afterCross = takerAmount;
                if (flow.Succeeded)
                    afterCross = AfterCross(psb, account, takerAmount, flow, gatewayRate, sell, rules);

                return ("tesSUCCESS", afterCross, flow);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException or DivideByZeroException)
            {
                return ("tecINTERNAL", takerAmount, null);
            }
        }

        /// <summary>What is left of the offer, at its original quality, once the crossing is done.</summary>
        private static (XrplAmount In, XrplAmount Out) AfterCross(
            DexView psb,
            string account,
            (XrplAmount In, XrplAmount Out) takerAmount,
            FlowResult flow,
            uint gatewayRate,
            bool sell,
            LedgerRules rules)
        {
            XrplAmount takerInBalance = psb.AccountFunds(account, takerAmount.In);
            if (StepMath.IsNotPositive(takerInBalance))
                return (XrplAmount.Zero(takerAmount.In.Asset), XrplAmount.Zero(takerAmount.Out.Asset));

            XrplAmount rate = XrplQuality.FromAmounts(takerAmount.In, takerAmount.Out, rules).RateAmount;
            if (sell)
            {
                // Only what reached the offers counts, not the gateway's cut.
                XrplAmount nonGatewayIn = gatewayRate == OfferCrossing.QualityOne
                    ? flow.In
                    : XrplAmountMath.DivRound(flow.In, StepMath.RateAmount(gatewayRate), takerAmount.In.Asset, roundUp: true, rules);

                XrplAmount remainingIn = XrplAmountMath.Subtract(takerAmount.In, nonGatewayIn, rules);
                if (remainingIn.IsNegative)
                    remainingIn = XrplAmount.Zero(takerAmount.In.Asset);

                return (remainingIn, XrplAmountMath.DivRoundStrict(remainingIn, rate, takerAmount.Out.Asset, roundUp: false, rules));
            }

            XrplAmount remainingOut = XrplAmountMath.Subtract(takerAmount.Out, flow.Out, rules);
            if (remainingOut.IsNegative)
                remainingOut = XrplAmount.Zero(takerAmount.Out.Asset);

            return (XrplAmountMath.MulRound(remainingOut, rate, takerAmount.In.Asset, roundUp: true, rules), remainingOut);
        }

        /// <summary>
        /// <c>toStrands</c> for offer crossing: the direct book, and when neither side is XRP a
        /// second strand bridged through XRP, each wrapped in the steps that move the taker's funds.
        /// </summary>
        private static List<List<FlowStep>> BuildStrands(
            DexView view,
            string account,
            IssuedCurrency @in,
            IssuedCurrency @out,
            XrplQuality threshold,
            AmmFlowContext ammContext)
        {
            List<List<FlowStep>> strands = new List<List<FlowStep>> { BuildStrand(view, account, @in, @out, threshold, ammContext, bridged: false) };
            if (XrplAmount.KindOf(@in) != AmountKind.Xrp && XrplAmount.KindOf(@out) != AmountKind.Xrp)
                strands.Add(BuildStrand(view, account, @in, @out, threshold, ammContext, bridged: true));

            return strands;
        }

        private static List<FlowStep> BuildStrand(
            DexView view,
            string account,
            IssuedCurrency @in,
            IssuedCurrency @out,
            XrplQuality threshold,
            AmmFlowContext ammContext,
            bool bridged)
        {
            List<FlowStep> strand = new List<FlowStep>();
            FlowStep first = null;
            if (XrplAmount.KindOf(@in) == AmountKind.Xrp)
            {
                int reserveReduction = view.Read(HoldingKey.Of(account, @out)).Exists ? 0 : -1;
                first = new XrpEndpointCrossingStep(account, isLast: false, reserveReduction);
            }
            else if (!string.Equals(account, @in.Issuer, StringComparison.Ordinal))
            {
                first = new DirectCrossingStep(account, @in.Issuer, @in, isLast: false);
            }

            if (first != null)
                strand.Add(first);

            FlowStep previous = first;
            if (bridged)
            {
                IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
                previous = new BookCrossingStep(view, @in, xrp, account, account, previous, defaultPath: false, threshold, ammContext);
                strand.Add(previous);
                previous = new BookCrossingStep(view, xrp, @out, account, account, previous, defaultPath: false, threshold, ammContext);
                strand.Add(previous);
            }
            else
            {
                previous = new BookCrossingStep(view, @in, @out, account, account, previous, defaultPath: true, threshold, ammContext);
                strand.Add(previous);
            }

            if (XrplAmount.KindOf(@out) == AmountKind.Xrp)
                strand.Add(new XrpEndpointCrossingStep(account, isLast: true, reserveReduction: 0));
            else if (!string.Equals(account, @out.Issuer, StringComparison.Ordinal))
                strand.Add(new DirectCrossingStep(@out.Issuer, account, @out, isLast: true));

            return strand;
        }

        /// <summary>The result, with every offer and pool that ended up different from the snapshot.</summary>
        private static OfferCrossingResult Result(
            DexWorld world,
            DexView final,
            string engineResult,
            XrplAmount paid,
            XrplAmount got,
            (XrplAmount TakerPays, XrplAmount TakerGets)? placed)
        {
            List<OfferChange> offers = new List<OfferChange>();
            foreach (DexOffer offer in world.Snapshot.Offers)
            {
                if (offer == null)
                    continue;

                OfferState state = final.Offer(offer.Index);
                if (state.Deleted)
                    offers.Add(new OfferChange(offer.Index, offer.Account, null, null));
                else if (state.TakerPays != offer.TakerPays || state.TakerGets != offer.TakerGets)
                    offers.Add(new OfferChange(offer.Index, offer.Account, state.TakerPays, state.TakerGets));
            }

            List<AmmPoolChange> pools = new List<AmmPoolChange>();
            foreach (DexAmmPool pool in world.Pools)
            {
                XrplAmount balance = final.Read(HoldingKey.Of(pool.Account, pool.Balance.Asset)).Current;
                XrplAmount balance2 = final.Read(HoldingKey.Of(pool.Account, pool.Balance2.Asset)).Current;
                if (balance != pool.Balance || balance2 != pool.Balance2)
                    pools.Add(new AmmPoolChange(pool.Account, balance, balance2));
            }

            HashSet<string> poolAccounts = new HashSet<string>(StringComparer.Ordinal);
            foreach (DexAmmPool pool in world.Pools)
                poolAccounts.Add(pool.Account);

            HashSet<HoldingKey> keys = new HashSet<HoldingKey>(final.TouchedHoldings());
            if (world.Fee > 0)
                keys.Add(HoldingKey.Of(world.FeeAccount, new IssuedCurrency { Currency = "XRP" }));

            List<BalanceChange> balances = new List<BalanceChange>();
            foreach (HoldingKey key in keys)
            {
                if (poolAccounts.Contains(key.Account))
                    continue;

                XrplAmount change = XrplAmountMath.ExactDifference(final.Read(key).Current, final.SnapshotBalance(key));
                if (!change.IsZero)
                    balances.Add(new BalanceChange(key.Account, change));
            }

            balances.Sort((a, b) =>
            {
                int byAccount = string.CompareOrdinal(a.Account, b.Account);
                return byAccount != 0 ? byAccount : string.CompareOrdinal(a.Change.Asset?.Currency, b.Change.Asset?.Currency);
            });

            return new OfferCrossingResult(engineResult, paid, got, placed?.TakerPays, placed?.TakerGets, offers, pools, balances);
        }
    }

    /// <summary>What an <c>OfferCreate</c> would do, as <see cref="OfferCreateCrossing"/> computes it.</summary>
    public sealed class OfferCrossingResult
    {
        internal OfferCrossingResult(
            string engineResult,
            XrplAmount paid,
            XrplAmount received,
            XrplAmount? placedTakerPays,
            XrplAmount? placedTakerGets,
            IReadOnlyList<OfferChange> offers,
            IReadOnlyList<AmmPoolChange> pools,
            IReadOnlyList<BalanceChange> balances)
        {
            BalanceChanges = balances;
            EngineResult = engineResult;
            Paid = paid;
            Received = received;
            PlacedTakerPays = placedTakerPays;
            PlacedTakerGets = placedTakerGets;
            Offers = offers;
            Pools = pools;
        }

        /// <summary>The node's result: <c>tesSUCCESS</c>, <c>tecKILLED</c>, <c>tecUNFUNDED_OFFER</c>, <c>tecINSUF_RESERVE_OFFER</c> or <c>tecINTERNAL</c>.</summary>
        public string EngineResult { get; }

        /// <summary>
        /// What the account paid of its <c>TakerGets</c> asset, transfer fees included: the
        /// engine's total, the sum of what each iteration took. The account's balance moves by
        /// the same amount rounded at every iteration, which <see cref="BalanceChanges"/> reports.
        /// </summary>
        public XrplAmount Paid { get; }

        /// <summary>What the account received of its <c>TakerPays</c> asset: the engine's total, as <see cref="Paid"/>.</summary>
        public XrplAmount Received { get; }

        /// <summary>The <c>TakerPays</c> of the offer left in the book; null when none is placed.</summary>
        public XrplAmount? PlacedTakerPays { get; }

        /// <summary>The <c>TakerGets</c> of the offer left in the book; null when none is placed.</summary>
        public XrplAmount? PlacedTakerGets { get; }

        /// <summary>The snapshot's offers that were crossed or removed.</summary>
        public IReadOnlyList<OfferChange> Offers { get; }

        /// <summary>The AMM pools that were traded against.</summary>
        public IReadOnlyList<AmmPoolChange> Pools { get; }

        /// <summary>
        /// How every account's balances moved, the transaction's fee included, as the ledger
        /// records them - the same figures <c>BalanceChanges.GetBalanceChanges</c> reads from the
        /// metadata. Pools are in <see cref="Pools"/>; an issuer's side of its trust lines is left out.
        /// </summary>
        public IReadOnlyList<BalanceChange> BalanceChanges { get; }
    }

    /// <summary>How one account's balance of one asset moved.</summary>
    public sealed class BalanceChange
    {
        internal BalanceChange(string account, XrplAmount change)
        {
            Account = account;
            Change = change;
        }

        /// <summary>The account.</summary>
        public string Account { get; }

        /// <summary>The change: positive when the account received.</summary>
        public XrplAmount Change { get; }
    }

    /// <summary>An offer after the crossing.</summary>
    public sealed class OfferChange
    {
        internal OfferChange(string index, string account, XrplAmount? takerPays, XrplAmount? takerGets)
        {
            Index = index;
            Account = account;
            TakerPays = takerPays;
            TakerGets = takerGets;
        }

        /// <summary>The offer's ledger index.</summary>
        public string Index { get; }

        /// <summary>The offer's owner.</summary>
        public string Account { get; }

        /// <summary>Whether the offer left the ledger: fully taken, or removed as expired, unfunded or too small.</summary>
        public bool Deleted => TakerPays == null;

        /// <summary>What the offer still asks for; null when it was deleted.</summary>
        public XrplAmount? TakerPays { get; }

        /// <summary>What the offer still gives; null when it was deleted.</summary>
        public XrplAmount? TakerGets { get; }
    }

    /// <summary>An AMM pool's balances after the crossing.</summary>
    public sealed class AmmPoolChange
    {
        internal AmmPoolChange(string account, XrplAmount balance, XrplAmount balance2)
        {
            Account = account;
            Balance = balance;
            Balance2 = balance2;
        }

        /// <summary>The AMM's account.</summary>
        public string Account { get; }

        /// <summary>The pool's balance of its first asset.</summary>
        public XrplAmount Balance { get; }

        /// <summary>The pool's balance of its second asset.</summary>
        public XrplAmount Balance2 { get; }
    }
}
