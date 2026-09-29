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
    /// destination checks of <c>preclaim</c>, the credentials it presents, the permissioned
    /// domain, deposit authorization by account or by credentials), <c>RippleCalc</c> and
    /// <c>flow</c>: <c>toStrands</c> with its implied steps and loop checks, direct steps with
    /// their qualities, limits, NoRipple and transfer fees, book steps - the domain's books for
    /// a payment in a domain - and AMM pools, <c>StrandFlow</c>, and the trust lines a balance
    /// back at zero releases or deletes. A direct XRP payment is checked against the reserve.
    /// </para>
    /// <para>
    /// Not covered: MPT payments and sponsored reserves. The paths come with the payment, as
    /// <c>ripple_path_find</c> returns them or <see cref="PathFinding"/> finds them. A result is
    /// exact only against the state it was given.
    /// </para>
    /// </remarks>
    public static class PaymentFlow
    {
        private const int MaxPathSize = 6;
        private const int MaxPathLength = 8;
        private const int MaxCredentials = 8;

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
            if (PreflightExtras(payment, rules) is { } malformedExtras)
                return NotApplied(snapshot, malformedExtras);

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

            if (CheckCredentials(world, payment.CredentialIDs, account) is { } badCredentials)
                return Applied(world, new DexView(world, rules), badCredentials, null, null);
            if (CheckDomain(world, payment.DomainID, account, destination, rules) is { } notInDomain)
                return Applied(world, new DexView(world, rules), notInDomain, null, null);

            // doApply: a member whose only matching credentials have expired is refused now.
            if (!string.IsNullOrEmpty(payment.DomainID) && rules.FixCleanup3_4_0 &&
                (VerifyDomain(world, payment.DomainID, account) ?? VerifyDomain(world, payment.DomainID, destination)) is { } expired)
            {
                return Applied(world, new DexView(world, rules), expired, null, null);
            }

            // The destination account is created before any funds move.
            if (!destinationExists)
                world.Accounts[destination] = destinationAccount = new DexAccount { Address = destination };

            DexView view = new DexView(world, rules);
            return ripple
                ? Ripple(world, view, payment, account, destination, destinationAccount, amount, maxSource, deliverMin, paths, partialPayment, limitQuality, defaultPaths, rules)
                : DirectXrp(world, view, payment, account, destination, destinationAccount, amount, fee);
        }

        /// <summary>The credential and domain fields' own malformations (<c>credentials::checkFields</c>).</summary>
        private static string PreflightExtras(Payment payment, LedgerRules rules)
        {
            if (rules.FixCleanup3_2_0 && payment.DomainID != null && payment.DomainID.Trim('0').Length == 0)
                return "temMALFORMED";

            if (payment.CredentialIDs == null)
                return null;
            if (payment.CredentialIDs.Count == 0 || payment.CredentialIDs.Count > MaxCredentials)
                return "temMALFORMED";

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in payment.CredentialIDs)
            {
                if (rules.FixCleanup3_4_0 && (id == null || id.Trim('0').Length == 0))
                    return "temMALFORMED";
                if (!seen.Add(id ?? string.Empty))
                    return "temMALFORMED";
            }

            return null;
        }

        /// <summary><c>credentials::valid</c>: each credential presented exists, is the sender's, and was accepted.</summary>
        private static string CheckCredentials(DexWorld world, List<string> credentialIds, string account)
        {
            foreach (string id in credentialIds ?? new List<string>())
            {
                if (!world.CredentialsById.TryGetValue(id, out DexCredential credential) ||
                    !string.Equals(credential.Subject, account, StringComparison.Ordinal) ||
                    !credential.Accepted)
                {
                    return "tecBAD_CREDENTIALS";
                }
            }

            return null;
        }

        /// <summary><c>Payment::preclaim</c>'s domain check: the sender and the destination are both members.</summary>
        private static string CheckDomain(DexWorld world, string domainId, string account, string destination, LedgerRules rules)
        {
            if (string.IsNullOrEmpty(domainId))
                return null;

            foreach (string member in new[] { account, destination })
            {
                if (rules.FixCleanup3_4_0)
                {
                    // Expired credentials pass here; doApply refuses them.
                    string valid = world.ValidDomain(domainId, member);
                    if (valid == "tecOBJECT_NOT_FOUND" || (valid != null && valid != "tecEXPIRED"))
                        return "tecNO_PERMISSION";
                }
                else if (!world.AccountInDomain(member, domainId))
                {
                    return "tecNO_PERMISSION";
                }
            }

            return null;
        }

        /// <summary><c>verifyValidDomain</c>: null for a member, <c>tecEXPIRED</c> when its matching credentials have expired.</summary>
        private static string VerifyDomain(DexWorld world, string domainId, string member) =>
            world.ValidDomain(domainId, member) switch
            {
                null => null,
                "tecEXPIRED" => "tecEXPIRED",
                _ => "tecNO_PERMISSION",
            };

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
            if (VerifyDepositPreauth(world, payment.CredentialIDs, account, destination, destinationAccount) is { } refused)
                return Applied(world, new DexView(world, rules), refused, null, null);

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
                DomainId = payment.DomainID,
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
            Payment payment,
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
                VerifyDepositPreauth(world, payment.CredentialIDs, account, destination, destinationAccount) is { } refused)
            {
                return Applied(world, new DexView(world, view.Rules), refused, null, null);
            }

            view.Send(account, string.Empty, amount);
            view.Send(string.Empty, destination, amount);
            return Applied(world, view, "tesSUCCESS", amount, amount);
        }

        /// <summary>
        /// <c>verifyDepositPreauth</c>: expired credentials refuse the payment; then a destination
        /// with deposit authorization takes only from itself, from the accounts it preauthorized,
        /// and from a sender presenting a set of credentials it preauthorized.
        /// </summary>
        private static string VerifyDepositPreauth(
            DexWorld world,
            List<string> credentialIds,
            string account,
            string destination,
            DexAccount destinationAccount)
        {
            foreach (string id in credentialIds ?? new List<string>())
            {
                if (world.CredentialsById.TryGetValue(id, out DexCredential credential) && world.Expired(credential))
                    return "tecEXPIRED";
            }

            if (!destinationAccount.DepositAuth || string.Equals(account, destination, StringComparison.Ordinal))
                return null;

            foreach (string preauthorized in destinationAccount.DepositPreauthorized ?? Array.Empty<string>())
            {
                if (string.Equals(preauthorized, account, StringComparison.Ordinal))
                    return null;
            }

            if (credentialIds == null)
                return "tecNO_PERMISSION";

            return PreauthorizedCredentials(world, credentialIds, destinationAccount) ? null : "tecNO_PERMISSION";
        }

        /// <summary><c>authorizedDepositPreauth</c>: the credentials presented, as a set of issuer and type, are one the destination preauthorized.</summary>
        private static bool PreauthorizedCredentials(DexWorld world, List<string> credentialIds, DexAccount destinationAccount)
        {
            HashSet<(string Issuer, string Type)> presented = new HashSet<(string Issuer, string Type)>();
            foreach (string id in credentialIds)
            {
                DexCredential credential = world.CredentialsById[id];
                if (!presented.Add((credential.Issuer, credential.CredentialType.ToUpperInvariant())))
                    return false;
            }

            foreach (IReadOnlyList<DexCredentialType> set in destinationAccount.DepositPreauthorizedCredentials ?? Array.Empty<IReadOnlyList<DexCredentialType>>())
            {
                HashSet<(string Issuer, string Type)> accepted = new HashSet<(string Issuer, string Type)>();
                foreach (DexCredentialType type in set ?? Array.Empty<DexCredentialType>())
                    accepted.Add((type.Issuer, type.CredentialType?.ToUpperInvariant()));

                if (accepted.SetEquals(presented))
                    return true;
            }

            return false;
        }

        private static PaymentFlowResult Applied(DexWorld world, DexView final, string result, XrplAmount? delivered, XrplAmount? paid) =>
            new PaymentFlowResult(result, true, delivered, paid, LedgerChanges.Collect(world, final));

        private static PaymentFlowResult NotApplied(DexSnapshot snapshot, string result) =>
            new PaymentFlowResult(result, false, null, null, LedgerChanges.None);

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
            LedgerChanges changes)
        {
            EngineResult = engineResult;
            Applied = applied;
            DeliveredAmount = delivered;
            Paid = paid;
            Offers = changes.Offers;
            Pools = changes.Pools;
            BalanceChanges = changes.Balances;
            TrustLines = changes.TrustLines;
            NeedsDeeperBooks = changes.ReachedPartialBook;
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

        /// <summary>The trust lines the payment created or deleted.</summary>
        public IReadOnlyList<TrustLineChange> TrustLines { get; }

        /// <summary>
        /// Whether the engine walked past the last offer the snapshot read of a book it holds
        /// only in part (<see cref="DexSnapshot.PartialBooks"/>): the node may cross offers the
        /// snapshot does not have, so the result is exact only once the snapshot is read deeper.
        /// </summary>
        public bool NeedsDeeperBooks { get; }
    }
}
