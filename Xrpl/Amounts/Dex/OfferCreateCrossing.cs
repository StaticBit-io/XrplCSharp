using System;
using System.Collections.Generic;
using System.Globalization;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils.Hashes;

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
    /// The crossing repeats <c>OfferCreate</c>'s checks - the <c>tem</c> codes of
    /// <c>preflight</c>, the frozen, unfunded, expired and unauthorized cases of
    /// <c>preclaim</c>, the permissioned domain - then <c>OfferSequence</c>'s cancellation and
    /// <c>OfferCreate::flowCross</c> with the engine under it: strands, book steps, the AMM's
    /// synthetic offers and <c>StrandFlow</c>, including their rounding, so against the same
    /// state the amounts match the node's to the last digit.
    /// </para>
    /// <para>
    /// Not covered: MPT books and sponsored reserves. A result is exact only against the state
    /// it was given; <c>simulate</c> remains the reference for a transaction that is about to be
    /// submitted.
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
        /// <exception cref="ArgumentException">The account is not in the snapshot, the two sides are the same asset, or the snapshot lists a trust line twice.</exception>
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

            Order order = new Order
            {
                Account = account,
                TakerPays = takerPays,
                TakerGets = takerGets,
                Fee = fee,
                Flags = flags,
            };
            return Apply(snapshot, order, rules ?? new LedgerRules());
        }

        /// <summary>What <paramref name="offer"/> would do against <paramref name="snapshot"/>.</summary>
        /// <remarks>
        /// With <c>OfferSequence</c>, the offer it cancels must be in the snapshot to be removed;
        /// <see cref="DexSnapshot.FromNodeAsync(Xrpl.Client.IXrplClient, OfferCreate, DexSnapshotOptions, System.Threading.CancellationToken)"/>
        /// reads it.
        /// </remarks>
        /// <param name="snapshot">The ledger state to cross against.</param>
        /// <param name="offer">The transaction, with its <c>Fee</c> set.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The offer lacks its account or amounts, the account is not in the snapshot, or the snapshot lists a trust line twice.</exception>
        /// <exception cref="NotSupportedException">A side is an MPT.</exception>
        public static OfferCrossingResult Cross(DexSnapshot snapshot, OfferCreate offer, LedgerRules rules = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (offer?.Account == null || offer.TakerPays == null || offer.TakerGets == null)
                throw new ArgumentException("An offer needs Account, TakerPays and TakerGets.", nameof(offer));
            if (offer.TakerPays.MPTokenIssuanceID != null || offer.TakerGets.MPTokenIssuanceID != null)
                throw new NotSupportedException("MPT offers are not supported.");

            Order order = new Order
            {
                Account = offer.Account,
                TakerPays = offer.TakerPays.ToXrplAmount(),
                TakerGets = offer.TakerGets.ToXrplAmount(),
                Fee = offer.Fee?.Value is { } feeText ? ulong.Parse(feeText, CultureInfo.InvariantCulture) : 0,
                Flags = offer.Flags ?? 0,
                Sequence = offer.Sequence ?? 0,
                OfferSequence = offer.OfferSequence,
                Expiration = offer.Expiration is { } expiration ? (uint)LendingMath.RippleSeconds(expiration) : null,
                DomainId = offer.DomainID,
            };

            rules ??= new LedgerRules();
            return Preflight(order, rules) is { } malformed
                ? NotApplied(malformed, order)
                : Apply(snapshot, order, rules);
        }

        /// <summary>An <c>OfferCreate</c>'s fields, as the engine reads them.</summary>
        private sealed class Order
        {
            internal string Account { get; init; }

            internal XrplAmount TakerPays { get; init; }

            internal XrplAmount TakerGets { get; init; }

            internal ulong Fee { get; init; }

            internal OfferCreateFlags Flags { get; init; }

            internal uint Sequence { get; init; }

            internal uint? OfferSequence { get; init; }

            internal uint? Expiration { get; init; }

            internal string DomainId { get; init; }
        }

        /// <summary><c>OfferCreate::preflight</c>: the malformed offers, refused before any fee.</summary>
        private static string Preflight(Order order, LedgerRules rules)
        {
            OfferCreateFlags flags = order.Flags;
            if (flags.HasFlag(OfferCreateFlags.tfHybrid) && string.IsNullOrEmpty(order.DomainId))
                return "temINVALID_FLAG";
            if (rules.FixCleanup3_2_0 && order.DomainId != null && order.DomainId.Trim('0').Length == 0)
                return "temMALFORMED";
            if (flags.HasFlag(OfferCreateFlags.tfImmediateOrCancel) && flags.HasFlag(OfferCreateFlags.tfFillOrKill))
                return "temINVALID_FLAG";
            if (order.Expiration == 0)
                return "temBAD_EXPIRATION";
            if (order.OfferSequence == 0)
                return "temBAD_SEQUENCE";
            if (order.TakerPays.Kind == AmountKind.Xrp && order.TakerGets.Kind == AmountKind.Xrp)
                return "temBAD_OFFER";
            if (StepMath.IsNotPositive(order.TakerPays) || StepMath.IsNotPositive(order.TakerGets))
                return "temBAD_OFFER";
            if (XrplAmount.SameAsset(order.TakerPays.Asset, order.TakerGets.Asset))
                return "temREDUNDANT";
            if (IsBadCurrency(order.TakerPays) || IsBadCurrency(order.TakerGets))
                return "temBAD_CURRENCY";

            return null;
        }

        /// <summary>An issued currency may not use the code <c>XRP</c>.</summary>
        private static bool IsBadCurrency(XrplAmount amount) =>
            amount.Kind == AmountKind.Iou && string.Equals(amount.Asset.Currency, "XRP", StringComparison.Ordinal);

        /// <summary><c>OfferCreate::preclaim</c>, against the ledger before the fee.</summary>
        private static string Preclaim(DexSnapshot snapshot, Order order, DexAccount creator, LedgerRules rules)
        {
            DexView view = new DexView(new DexWorld(snapshot), rules);
            DexWorld world = view.World;
            if (IsGloballyFrozen(world, order.TakerPays.Asset) || IsGloballyFrozen(world, order.TakerGets.Asset))
                return "tecFROZEN";

            if (StepMath.IsNotPositive(view.AccountFunds(order.Account, order.TakerGets)))
                return "tecUNFUNDED_OFFER";

            uint accountSequence = creator.Sequence != 0 ? creator.Sequence : order.Sequence;
            if (order.OfferSequence is { } cancel && accountSequence != 0 && accountSequence <= cancel)
                return "temBAD_SEQUENCE";

            if (Expired(snapshot, order.Expiration))
                return "tecEXPIRED";

            if (order.TakerPays.Kind == AmountKind.Iou && CheckAcceptAsset(view, order.Account, order.TakerPays.Asset) is { } refused)
                return refused;

            if (!string.IsNullOrEmpty(order.DomainId))
            {
                if (rules.FixCleanup3_4_0)
                {
                    // Expired credentials pass here; the transaction itself refuses them.
                    string valid = world.ValidDomain(order.DomainId, order.Account);
                    if (valid == "tecOBJECT_NOT_FOUND" || (valid != null && valid != "tecEXPIRED"))
                        return "tecNO_PERMISSION";
                }
                else if (!world.AccountInDomain(order.Account, order.DomainId))
                {
                    return "tecNO_PERMISSION";
                }
            }

            return null;
        }

        private static bool IsGloballyFrozen(DexWorld world, IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Iou && world.Accounts.TryGetValue(asset.Issuer, out DexAccount issuer) && issuer.GlobalFreeze;

        /// <summary><c>hasExpired</c>: the parent ledger closed at or after the expiration.</summary>
        private static bool Expired(DexSnapshot snapshot, uint? expiration) =>
            expiration is { } at && snapshot.ParentCloseTime >= at;

        /// <summary><c>checkAcceptAsset</c>: whether the account may receive what it asks for.</summary>
        private static string CheckAcceptAsset(DexView view, string account, IssuedCurrency asset)
        {
            if (!view.World.Accounts.TryGetValue(asset.Issuer, out DexAccount issuer))
                return "tecNO_ISSUER";
            if (string.Equals(asset.Issuer, account, StringComparison.Ordinal))
                return null;

            LineInfo line = view.LineInfo(account, asset.Issuer, asset.Currency);
            if (view.Rules.FixCleanup3_4_0 && issuer.DisallowIncomingTrustline && line == null)
                return "tecNO_LINE";

            if (issuer.RequireAuth)
            {
                if (line == null)
                    return "tecNO_LINE";
                if (!line.Side(asset.Issuer).Auth)
                    return "tecNO_AUTH";
            }

            return line != null && line.DeepFrozen ? "tecFROZEN" : null;
        }

        /// <summary><c>OfferCreate::doApply</c> and <c>applyGuts</c>.</summary>
        private static OfferCrossingResult Apply(DexSnapshot snapshot, Order order, LedgerRules rules)
        {
            string account = order.Account;
            XrplAmount takerPays = order.TakerPays;
            XrplAmount takerGets = order.TakerGets;
            OfferCreateFlags flags = order.Flags;

            DexWorld world = new DexWorld(snapshot, account, order.Fee);
            if (!world.Accounts.TryGetValue(account, out DexAccount creator))
                throw new ArgumentException("The account is not in the snapshot.", nameof(order));

            if (Preclaim(snapshot, order, creator, rules) is { } refused)
                return refused.StartsWith("tec", StringComparison.Ordinal) ? FeeOnly(world, rules, refused, order) : NotApplied(refused, order);

            // A member whose only matching credentials have expired is refused here, with the fee.
            if (!string.IsNullOrEmpty(order.DomainId) && rules.FixCleanup3_4_0 && world.ValidDomain(order.DomainId, account) is { } domain)
                return FeeOnly(world, rules, domain == "tecEXPIRED" ? domain : "tecNO_PERMISSION", order);

            DexView ledger = new DexView(world, rules);
            DexView cancel = new DexView(world, rules);

            // The offer named by OfferSequence goes first; not finding it is no error.
            if (order.OfferSequence is { } cancelSequence)
            {
                string cancelled = Hashes.HashOfferId(account, cancelSequence);
                foreach (DexOffer existing in snapshot.Offers)
                {
                    if (existing != null && string.Equals(existing.Index, cancelled, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.Account, account, StringComparison.Ordinal))
                    {
                        ledger.DeleteOffer(existing.Index);
                        break;
                    }
                }
            }

            if (Expired(snapshot, order.Expiration))
                return FeeOnly(world, rules, "tecEXPIRED", order);

            bool sell = flags.HasFlag(OfferCreateFlags.tfSell);
            if (!RoundToTickSize(world, ref takerPays, ref takerGets, sell, rules))
                return Result(world, ledger, "tesSUCCESS", XrplAmount.Zero(takerGets.Asset), XrplAmount.Zero(takerPays.Asset), null);

            // Crossing, the account is the taker: it pays in what the offer gives.
            (XrplAmount In, XrplAmount Out) takerAmount = (takerGets, takerPays);
            DexView flowView = new DexView(ledger);
            DexView flowCancel = new DexView(cancel);
            (string result, (XrplAmount In, XrplAmount Out) placeOffer, FlowResult flow) =
                FlowCross(flowView, flowCancel, account, takerAmount, flags, order.DomainId, rules);
            flowView.ApplyTo(ledger);
            flowCancel.ApplyTo(cancel);

            XrplAmount paid = flow?.Succeeded == true ? flow.In : XrplAmount.Zero(takerGets.Asset);
            XrplAmount got = flow?.Succeeded == true ? flow.Out : XrplAmount.Zero(takerPays.Asset);
            if (result != "tesSUCCESS")
                return FeeOnly(world, rules, result, order);

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

            // The reserve for one more object, against the balance before the fee. The owner
            // count is the ledger's after the crossing, trust lines it created or cleared included.
            decimal reserve = snapshot.ReserveBase + (decimal)snapshot.ReserveIncrement * (ledger.OwnerCount(account) + 1);
            if (creator.Balance < reserve)
            {
                return crossed
                    ? Result(world, ledger, "tesSUCCESS", paid, got, null)
                    : FeeOnly(world, rules, "tecINSUF_RESERVE_OFFER", order);
            }

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
            string domainId,
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

                // The default path, and for two issued currencies a second one through XRP.
                AmmFlowContext ammContext = new AmmFlowContext(account);
                List<IReadOnlyList<PathElement>> paths = new List<IReadOnlyList<PathElement>>();
                if (takerAmount.In.Kind != AmountKind.Xrp && takerAmount.Out.Kind != AmountKind.Xrp)
                    paths.Add(new[] { new PathElement(null, "XRP", null) });

                StrandBuilder.Request request = new StrandBuilder.Request
                {
                    Source = account,
                    Destination = account,
                    Deliver = takerAmount.Out.Asset,
                    SendMax = takerAmount.In.Asset,
                    LimitQuality = threshold,
                    OfferCrossing = true,
                    AmmContext = ammContext,
                    DomainId = domainId,
                };
                (string strandsResult, List<List<FlowStep>> strands) = StrandBuilder.ToStrands(psb, request, paths, addDefaultPath: true);
                FlowResult flow;
                if (strandsResult != null)
                {
                    flow = new FlowResult { Result = strandsResult, RemovableOffers = new HashSet<string>(StringComparer.Ordinal) };
                }
                else
                {
                    ammContext.MultiPath = strands.Count > 1;
                    flow = StrandFlow.Run(
                        psb,
                        strands,
                        takerAmount.In.Asset,
                        deliver,
                        partialPayment: !flags.HasFlag(OfferCreateFlags.tfFillOrKill),
                        sell ? CrossingMode.Sell : CrossingMode.Yes,
                        threshold,
                        sendMax,
                        ammContext);
                }

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

        /// <summary>The result, with every offer, pool, balance and trust line that ended up different from the snapshot.</summary>
        private static OfferCrossingResult Result(
            DexWorld world,
            DexView final,
            string engineResult,
            XrplAmount paid,
            XrplAmount got,
            (XrplAmount TakerPays, XrplAmount TakerGets)? placed)
        {
            LedgerChanges changes = LedgerChanges.Collect(world, final);
            return new OfferCrossingResult(engineResult, true, paid, got, placed?.TakerPays, placed?.TakerGets, changes);
        }

        /// <summary>A <c>tec</c> result: the fee is charged and nothing else stays.</summary>
        private static OfferCrossingResult FeeOnly(DexWorld world, LedgerRules rules, string engineResult, Order order)
        {
            LedgerChanges changes = LedgerChanges.Collect(world, new DexView(world, rules));
            return new OfferCrossingResult(
                engineResult, true, XrplAmount.Zero(order.TakerGets.Asset), XrplAmount.Zero(order.TakerPays.Asset), null, null, changes);
        }

        /// <summary>A result that never reaches a ledger: nothing changes, not even the fee.</summary>
        private static OfferCrossingResult NotApplied(string engineResult, Order order) =>
            new OfferCrossingResult(
                engineResult, false, XrplAmount.Zero(order.TakerGets.Asset), XrplAmount.Zero(order.TakerPays.Asset), null, null, LedgerChanges.None);
    }

    /// <summary>What an <c>OfferCreate</c> would do, as <see cref="OfferCreateCrossing"/> computes it.</summary>
    public sealed class OfferCrossingResult
    {
        internal OfferCrossingResult(
            string engineResult,
            bool applied,
            XrplAmount paid,
            XrplAmount received,
            XrplAmount? placedTakerPays,
            XrplAmount? placedTakerGets,
            LedgerChanges changes)
        {
            EngineResult = engineResult;
            Applied = applied;
            Paid = paid;
            Received = received;
            PlacedTakerPays = placedTakerPays;
            PlacedTakerGets = placedTakerGets;
            Offers = changes.Offers;
            Pools = changes.Pools;
            BalanceChanges = changes.Balances;
            TrustLines = changes.TrustLines;
            NeedsDeeperBooks = changes.ReachedPartialBook;
        }

        /// <summary>
        /// The node's result: <c>tesSUCCESS</c>, <c>tecKILLED</c>, <c>tecUNFUNDED_OFFER</c>,
        /// <c>tecINSUF_RESERVE_OFFER</c>, <c>tecEXPIRED</c>, <c>tecFROZEN</c>, <c>tecNO_AUTH</c>,
        /// <c>tecNO_PERMISSION</c>, a <c>tem</c> code for a malformed offer, and so on.
        /// </summary>
        public string EngineResult { get; }

        /// <summary>Whether the transaction reaches a ledger and its fee is charged: <c>tes</c> and <c>tec</c> results.</summary>
        public bool Applied { get; }

        /// <summary>
        /// What the account paid of its <c>TakerGets</c> asset, transfer fees included: the
        /// engine's total, the sum of what each iteration took. The account's balance moves by
        /// the same amount rounded at every iteration, which <see cref="BalanceChanges"/> reports.
        /// Zero when the transaction fails.
        /// </summary>
        public XrplAmount Paid { get; }

        /// <summary>What the account received of its <c>TakerPays</c> asset: the engine's total, as <see cref="Paid"/>. Zero when the transaction fails.</summary>
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
        /// metadata, both sides of each trust line included. Pools are in <see cref="Pools"/>.
        /// </summary>
        public IReadOnlyList<BalanceChange> BalanceChanges { get; }

        /// <summary>The trust lines the transaction created or deleted.</summary>
        public IReadOnlyList<TrustLineChange> TrustLines { get; }

        /// <summary>
        /// Whether the engine walked past the last offer the snapshot read of a book it holds
        /// only in part (<see cref="DexSnapshot.PartialBooks"/>): the node may cross offers the
        /// snapshot does not have, so the result is exact only once the snapshot is read deeper.
        /// </summary>
        public bool NeedsDeeperBooks { get; }
    }

    /// <summary>A trust line the transaction created or deleted.</summary>
    public sealed class TrustLineChange
    {
        internal TrustLineChange(string account, string peer, string currency, bool created)
        {
            Account = account;
            Peer = peer;
            Currency = currency;
            Created = created;
        }

        /// <summary>One of the line's accounts: the lower in ordinal order.</summary>
        public string Account { get; }

        /// <summary>The line's other account.</summary>
        public string Peer { get; }

        /// <summary>The line's currency code.</summary>
        public string Currency { get; }

        /// <summary>True when the line was created, false when it was deleted.</summary>
        public bool Created { get; }

        /// <summary>Whether the line was deleted.</summary>
        public bool Deleted => !Created;
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
