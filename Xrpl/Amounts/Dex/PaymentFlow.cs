using System;
using System.Collections.Generic;
using System.Globalization;

using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// A <c>Payment</c> computed locally against a <see cref="DexSnapshot"/>, the way rippled
    /// 3.4.0 applies it: its paths turned into strands of trust lines, order books and AMM pools,
    /// run by the payment engine with <c>SendMax</c>, <c>DeliverMin</c>, partial payment and the
    /// limit quality, and the result the node would return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeated are <c>Payment</c>'s checks (the <c>tem</c> codes of <c>preflight</c>, the
    /// destination checks of <c>preclaim</c>, deposit authorization), <c>RippleCalc</c> and
    /// <c>flow</c>: <c>toStrands</c> with its implied steps and loop checks, direct steps with
    /// their qualities, limits, NoRipple and transfer fees, book steps and AMM pools, and
    /// <c>StrandFlow</c>. A direct XRP payment is checked against the reserve.
    /// </para>
    /// <para>
    /// Not covered: MPT payments, permissioned-DEX domains, credentials, deposit preauthorization
    /// by credential, and trust lines deleted or cleared when a balance returns to zero. Path
    /// finding is not part of it: the paths come with the payment, as <c>ripple_path_find</c> or
    /// <c>path_find</c> return them. A result is exact only against the state it was given.
    /// </para>
    /// </remarks>
    public static class PaymentFlow
    {
        private const int MaxPathSize = 6;
        private const int MaxPathLength = 8;

        /// <summary>What <paramref name="payment"/> would do against <paramref name="snapshot"/>.</summary>
        /// <param name="snapshot">The ledger state; it must hold the sender, the accounts and lines on the paths, and the books they cross.</param>
        /// <param name="payment">The payment, with its <c>Fee</c> set.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">The sender is not in the snapshot, or the snapshot lists a trust line twice.</exception>
        /// <exception cref="NotSupportedException">The payment moves an MPT.</exception>
        public static PaymentFlowResult Evaluate(DexSnapshot snapshot, Payment payment, LedgerRules rules = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (payment == null)
                throw new ArgumentNullException(nameof(payment));
            if (payment.Amount == null || payment.Account == null || payment.Destination == null)
                throw new ArgumentException("A payment needs Account, Destination and Amount.", nameof(payment));

            rules ??= new LedgerRules();
            string account = payment.Account;
            string destination = payment.Destination;
            XrplAmount amount = Read(payment.Amount);
            XrplAmount? sendMax = payment.SendMax == null ? null : Read(payment.SendMax);
            XrplAmount? deliverMin = payment.DeliverMin == null ? null : Read(payment.DeliverMin);
            PaymentFlags flags = payment.Flags ?? 0;
            bool partialPayment = flags.HasFlag(PaymentFlags.tfPartialPayment);
            bool limitQuality = flags.HasFlag(PaymentFlags.tfLimitQuality);
            bool defaultPaths = !flags.HasFlag(PaymentFlags.tfNoDirectRipple);
            List<IReadOnlyList<PathElement>> paths = Paths(payment.Paths);
            bool hasPaths = paths.Count > 0;
            ulong fee = payment.Fee?.Value is { } feeText ? ulong.Parse(feeText, CultureInfo.InvariantCulture) : 0;

            // getMaxSourceAmount: SendMax, or the amount as the sender's own issue.
            XrplAmount maxSource = sendMax ?? (amount.Kind == AmountKind.Xrp
                ? amount
                : amount.WithAsset(new IssuedCurrency { Currency = amount.Asset.Currency, Issuer = account }));

            if (Preflight(account, destination, amount, sendMax, maxSource, deliverMin, hasPaths, partialPayment, limitQuality, defaultPaths) is { } malformed)
                return NotApplied(snapshot, malformed);

            DexWorld world = new DexWorld(snapshot, account, fee);
            if (!world.Accounts.ContainsKey(account))
                throw new ArgumentException("The sender is not in the snapshot.", nameof(payment));

            bool destinationExists = world.Accounts.TryGetValue(destination, out DexAccount destinationAccount);
            if (!destinationExists)
            {
                if (amount.Kind != AmountKind.Xrp)
                    return Applied(world, new DexView(world, rules), "tecNO_DST", null, null);
                if (partialPayment)
                    return NotApplied(snapshot, "telNO_DST_PARTIAL");
                if (amount.StMantissa < snapshot.ReserveBase)
                    return Applied(world, new DexView(world, rules), "tecNO_DST_INSUF_XRP", null, null);
            }
            else if (destinationAccount.RequireDestinationTag && payment.DestinationTag == null)
            {
                return Applied(world, new DexView(world, rules), "tecDST_TAG_NEEDED", null, null);
            }

            bool ripple = hasPaths || sendMax != null || amount.Kind != AmountKind.Xrp;
            if (ripple && (paths.Count > MaxPathSize || paths.Exists(p => p.Count > MaxPathLength)))
                return NotApplied(snapshot, "telBAD_PATH_COUNT");

            // The destination account is created before any funds move.
            if (!destinationExists)
                world.Accounts[destination] = destinationAccount = new DexAccount { Address = destination };

            DexView view = new DexView(world, rules);
            return ripple
                ? Ripple(world, view, payment, account, destination, destinationAccount, amount, maxSource, deliverMin, paths, partialPayment, limitQuality, defaultPaths, rules)
                : DirectXrp(world, view, account, destination, destinationAccount, amount, fee);
        }

        /// <summary><c>Payment::preflight</c>: the malformed payments, refused before any fee.</summary>
        private static string Preflight(
            string account,
            string destination,
            XrplAmount amount,
            XrplAmount? sendMax,
            XrplAmount maxSource,
            XrplAmount? deliverMin,
            bool hasPaths,
            bool partialPayment,
            bool limitQuality,
            bool defaultPaths)
        {
            bool xrpDirect = maxSource.Kind == AmountKind.Xrp && amount.Kind == AmountKind.Xrp;
            if (sendMax != null && StepMath.IsNotPositive(maxSource))
                return "temBAD_AMOUNT";
            if (StepMath.IsNotPositive(amount))
                return "temBAD_AMOUNT";
            if (string.Equals(account, destination, StringComparison.Ordinal) && SameToken(maxSource.Asset, amount.Asset) && !hasPaths)
                return "temREDUNDANT";
            if (xrpDirect && sendMax != null)
                return "temBAD_SEND_XRP_MAX";
            if (xrpDirect && hasPaths)
                return "temBAD_SEND_XRP_PATHS";
            if (xrpDirect && partialPayment)
                return "temBAD_SEND_XRP_PARTIAL";
            if (xrpDirect && limitQuality)
                return "temBAD_SEND_XRP_LIMIT";
            if (xrpDirect && !defaultPaths)
                return "temBAD_SEND_XRP_NO_DIRECT";

            if (deliverMin is { } min)
            {
                if (!partialPayment || StepMath.IsNotPositive(min) || !XrplAmount.SameAsset(min.Asset, amount.Asset) || min > amount)
                    return "temBAD_AMOUNT";
            }

            return null;
        }

        /// <summary><c>equalTokens</c>: two assets of the same currency, whoever issued them.</summary>
        private static bool SameToken(IssuedCurrency a, IssuedCurrency b)
        {
            AmountKind kind = XrplAmount.KindOf(a);
            if (kind != XrplAmount.KindOf(b))
                return false;

            return kind == AmountKind.Xrp || string.Equals(a.Currency, b.Currency, StringComparison.Ordinal);
        }

        /// <summary>The payment engine: <c>RippleCalc::rippleCalculate</c> and what <c>Payment::doApply</c> makes of it.</summary>
        private static PaymentFlowResult Ripple(
            DexWorld world,
            DexView view,
            Payment payment,
            string account,
            string destination,
            DexAccount destinationAccount,
            XrplAmount amount,
            XrplAmount maxSource,
            XrplAmount? deliverMin,
            List<IReadOnlyList<PathElement>> paths,
            bool partialPayment,
            bool limitQuality,
            bool defaultPaths,
            LedgerRules rules)
        {
            if (!DepositAllowed(account, destination, destinationAccount))
                return Applied(world, new DexView(world, rules), "tecNO_PERMISSION", null, null);

            XrplQuality? limit = limitQuality && StepMath.IsPositive(maxSource)
                ? XrplQuality.FromAmounts(maxSource, amount, rules)
                : null;

            AmmFlowContext ammContext = new AmmFlowContext(account);
            StrandBuilder.Request request = new StrandBuilder.Request
            {
                Source = account,
                Destination = destination,
                Deliver = amount.Asset,
                SendMax = maxSource.Asset,
                LimitQuality = limit,
                OfferCrossing = false,
                AmmContext = ammContext,
            };

            string result;
            FlowResult flow = null;
            try
            {
                (string strandsResult, List<List<FlowStep>> strands) = StrandBuilder.ToStrands(view, request, paths, defaultPaths);
                if (strandsResult != null)
                {
                    result = strandsResult;
                }
                else
                {
                    ammContext.MultiPath = strands.Count > 1;
                    flow = StrandFlow.Run(view, strands, maxSource.Asset, amount, partialPayment, CrossingMode.No, limit, maxSource, ammContext);
                    result = flow.Result;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException or DivideByZeroException)
            {
                result = "tecINTERNAL";
            }

            XrplAmount? delivered = null;
            if (result == "tesSUCCESS")
            {
                delivered = flow.Out;
                if (flow.Out != amount && deliverMin is { } min && flow.Out < min)
                    result = "tecPATH_PARTIAL";
            }

            // A retry code costs the fee instead: the paths are the sender's to get right.
            if (result.StartsWith("ter", StringComparison.Ordinal))
                result = "tecPATH_DRY";

            if (result == "tesSUCCESS")
            {
                flow.Sandbox.ApplyTo(view);
                return Applied(world, view, result, delivered, flow.In);
            }

            if (result.StartsWith("tec", StringComparison.Ordinal))
                return Applied(world, new DexView(world, rules), result, null, null);

            return NotApplied(world.Snapshot, result);
        }

        /// <summary>A direct XRP payment: the sender keeps its reserve, and the destination's deposit authorization holds.</summary>
        private static PaymentFlowResult DirectXrp(
            DexWorld world,
            DexView view,
            string account,
            string destination,
            DexAccount destinationAccount,
            XrplAmount amount,
            ulong fee)
        {
            DexSnapshot snapshot = world.Snapshot;
            DexAccount source = world.Accounts[account];
            decimal reserve = snapshot.ReserveBase + (decimal)snapshot.ReserveIncrement * source.OwnerCount;
            decimal required = amount.StMantissa + Math.Max(reserve, fee);
            if (source.Balance < required)
                return Applied(world, new DexView(world, view.Rules), "tecUNFUNDED_PAYMENT", null, null);

            if (world.PoolAccounts.Contains(destination))
                return Applied(world, new DexView(world, view.Rules), "tecNO_PERMISSION", null, null);

            if ((amount.StMantissa > snapshot.ReserveBase || destinationAccount.Balance > snapshot.ReserveBase) &&
                !DepositAllowed(account, destination, destinationAccount))
            {
                return Applied(world, new DexView(world, view.Rules), "tecNO_PERMISSION", null, null);
            }

            view.Send(account, string.Empty, amount);
            view.Send(string.Empty, destination, amount);
            return Applied(world, view, "tesSUCCESS", amount, amount);
        }

        /// <summary><c>checkDepositPreauth</c>: a destination with deposit authorization takes only from itself and those it preauthorized.</summary>
        private static bool DepositAllowed(string account, string destination, DexAccount destinationAccount)
        {
            if (!destinationAccount.DepositAuth || string.Equals(account, destination, StringComparison.Ordinal))
                return true;

            foreach (string preauthorized in destinationAccount.DepositPreauthorized ?? Array.Empty<string>())
            {
                if (string.Equals(preauthorized, account, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static PaymentFlowResult Applied(DexWorld world, DexView final, string result, XrplAmount? delivered, XrplAmount? paid)
        {
            (List<OfferChange> offers, List<AmmPoolChange> pools, List<BalanceChange> balances) = LedgerChanges.Collect(world, final);
            return new PaymentFlowResult(result, true, delivered, paid, offers, pools, balances);
        }

        private static PaymentFlowResult NotApplied(DexSnapshot snapshot, string result) =>
            new PaymentFlowResult(result, false, null, null, Array.Empty<OfferChange>(), Array.Empty<AmmPoolChange>(), Array.Empty<BalanceChange>());

        private static XrplAmount Read(Currency amount)
        {
            if (amount.MPTokenIssuanceID != null)
                throw new NotSupportedException("MPT payments are not supported.");

            return amount.ToXrplAmount();
        }

        /// <summary>The payment's paths as path elements; XRP is the currency "XRP" without an issuer.</summary>
        private static List<IReadOnlyList<PathElement>> Paths(List<List<PathStep>> paths)
        {
            List<IReadOnlyList<PathElement>> result = new List<IReadOnlyList<PathElement>>();
            foreach (List<PathStep> path in paths ?? new List<List<PathStep>>())
            {
                List<PathElement> elements = new List<PathElement>();
                foreach (PathStep step in path ?? new List<PathStep>())
                {
                    if (step.MPTokenIssuanceID != null)
                        throw new NotSupportedException("MPT paths are not supported.");

                    elements.Add(new PathElement(step.Account, step.CurrencyCode, step.Issuer));
                }

                result.Add(elements);
            }

            return result;
        }
    }

    /// <summary>What a <c>Payment</c> would do, as <see cref="PaymentFlow"/> computes it.</summary>
    public sealed class PaymentFlowResult
    {
        internal PaymentFlowResult(
            string engineResult,
            bool applied,
            XrplAmount? delivered,
            XrplAmount? paid,
            IReadOnlyList<OfferChange> offers,
            IReadOnlyList<AmmPoolChange> pools,
            IReadOnlyList<BalanceChange> balances)
        {
            EngineResult = engineResult;
            Applied = applied;
            DeliveredAmount = delivered;
            Paid = paid;
            Offers = offers;
            Pools = pools;
            BalanceChanges = balances;
        }

        /// <summary>The node's result, such as <c>tesSUCCESS</c>, <c>tecPATH_PARTIAL</c>, <c>tecPATH_DRY</c> or a <c>tem</c> code.</summary>
        public string EngineResult { get; }

        /// <summary>Whether the transaction reaches a ledger and its fee is charged: <c>tes</c> and <c>tec</c> results.</summary>
        public bool Applied { get; }

        /// <summary>What the destination received (<c>delivered_amount</c>); null unless the payment succeeds.</summary>
        public XrplAmount? DeliveredAmount { get; }

        /// <summary>What the sender spent in the SendMax asset, transfer fees included; null unless the payment succeeds.</summary>
        public XrplAmount? Paid { get; }

        /// <summary>The snapshot's offers that were crossed or removed.</summary>
        public IReadOnlyList<OfferChange> Offers { get; }

        /// <summary>The AMM pools the payment went through.</summary>
        public IReadOnlyList<AmmPoolChange> Pools { get; }

        /// <summary>How every account's balances moved, the fee included, as the metadata will record them.</summary>
        public IReadOnlyList<BalanceChange> BalanceChanges { get; }
    }
}
