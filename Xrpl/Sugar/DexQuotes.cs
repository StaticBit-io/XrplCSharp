using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// Quotes for payments and offers, computed by the local payment engine against the node's
    /// last validated ledger: a <see cref="DexSnapshot"/> read from the node, then
    /// <see cref="PaymentFlow"/> or <see cref="OfferCreateCrossing"/> run on it.
    /// </summary>
    /// <remarks>
    /// A quote is exact against the ledger it was read from; the transaction lands in a later
    /// one. Each book is first read one <c>book_offers</c> page deep; when the engine walks past
    /// what was read, the snapshot is read again, deeper, until the result no longer depends on
    /// unread offers or the deepest read is reached.
    /// </remarks>
    public static class DexQuoteSugar
    {
        /// <summary>The book depths tried in turn: one <c>book_offers</c> page, then deeper walks of the directories.</summary>
        private static readonly int?[] Depths = { null, 1_000, 5_000 };

        /// <summary>What <paramref name="payment"/> would do if it were applied now.</summary>
        /// <param name="client">The node to read from.</param>
        /// <param name="payment">The payment, with its paths; its <c>Fee</c> is charged when set.</param>
        /// <param name="rules">The amendments in force; read from the node when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<PaymentFlowResult> QuotePaymentAsync(
            this IXrplClient client,
            Payment payment,
            LedgerRules rules = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (payment == null)
                throw new ArgumentNullException(nameof(payment));

            rules ??= await LedgerRules.FromNodeAsync(client, cancellationToken).ConfigureAwait(false);
            PaymentFlowResult result = null;
            foreach (int? depth in Depths)
            {
                DexSnapshot snapshot = await DexSnapshot
                    .FromNodeAsync(client, payment, new DexSnapshotOptions { BookDepth = depth }, cancellationToken)
                    .ConfigureAwait(false);
                result = PaymentFlow.Evaluate(snapshot, payment, rules);
                if (!result.NeedsDeeperBooks)
                    break;
            }

            return result;
        }

        /// <summary>What <paramref name="offer"/> would do if it were applied now.</summary>
        /// <param name="client">The node to read from.</param>
        /// <param name="offer">The offer; its <c>Fee</c> is charged when set.</param>
        /// <param name="rules">The amendments in force; read from the node when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<OfferCrossingResult> QuoteOfferCreateAsync(
            this IXrplClient client,
            OfferCreate offer,
            LedgerRules rules = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (offer == null)
                throw new ArgumentNullException(nameof(offer));

            rules ??= await LedgerRules.FromNodeAsync(client, cancellationToken).ConfigureAwait(false);
            OfferCrossingResult result = null;
            foreach (int? depth in Depths)
            {
                DexSnapshot snapshot = await DexSnapshot
                    .FromNodeAsync(client, offer, new DexSnapshotOptions { BookDepth = depth }, cancellationToken)
                    .ConfigureAwait(false);
                result = OfferCreateCrossing.Cross(snapshot, offer, rules);
                if (!result.NeedsDeeperBooks)
                    break;
            }

            return result;
        }

        /// <summary>
        /// What delivering exactly <paramref name="deliver"/> to <paramref name="destination"/>
        /// costs <paramref name="source"/> in <paramref name="sourceAsset"/>, along the paths
        /// <c>ripple_path_find</c> finds - or, given <paramref name="books"/>, the local path
        /// finder (<see cref="PathFinding"/>).
        /// </summary>
        /// <remarks>
        /// The quote's <see cref="PaymentQuote.Payment"/> carries those paths and a <c>SendMax</c>
        /// of the quoted cost, so against an unchanged ledger it delivers exactly; raise
        /// <c>SendMax</c> to leave room for the market to move.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="source">The paying account.</param>
        /// <param name="destination">The receiving account.</param>
        /// <param name="deliver">The amount to deliver.</param>
        /// <param name="sourceAsset">The asset to pay with; an issued currency's issuer may be <paramref name="source"/> itself for any issuer it holds.</param>
        /// <param name="destinationTag">The payment's destination tag, for a destination that requires one.</param>
        /// <param name="books">The order books for a local path search; null to ask the node's <c>ripple_path_find</c>.</param>
        /// <param name="rules">The amendments in force; read from the node when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<PaymentQuote> QuoteDeliverAsync(
            this IXrplClient client,
            string source,
            string destination,
            XrplAmount deliver,
            IssuedCurrency sourceAsset,
            uint? destinationTag = null,
            IBookIndex books = null,
            LedgerRules rules = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (sourceAsset == null)
                throw new ArgumentNullException(nameof(sourceAsset));

            bool direct = deliver.Kind == AmountKind.Xrp && XrplAmount.KindOf(sourceAsset) == AmountKind.Xrp;
            IssuedCurrency sendMaxAsset = sourceAsset;
            List<List<PathStep>> paths = null;
            if (!direct)
            {
                PathFindRequest find = new PathFindRequest
                {
                    SourceAccount = source,
                    DestinationAccount = destination,
                    DestinationAmount = deliver,
                    SourceCurrencies = new[] { sourceAsset },
                };
                (XrplAmount SourceAmount, List<List<PathStep>> Paths)? alternative =
                    Matching(await FindAsync(client, find, books, rules, cancellationToken).ConfigureAwait(false), sourceAsset);
                if (alternative is { } found)
                    sendMaxAsset = found.SourceAmount.Asset;
                paths = NonEmpty(alternative?.Paths);
            }

            // Quoted with no ceiling, the engine takes what it needs, which is the cost.
            Payment quoted = Build(source, destination, deliver, direct ? null : Largest(sendMaxAsset), paths, 0, destinationTag);
            PaymentFlowResult result = await client.QuotePaymentAsync(quoted, rules, cancellationToken).ConfigureAwait(false);

            Payment payment = Build(source, destination, deliver, direct ? null : result.Paid, paths, 0, destinationTag);
            return new PaymentQuote(payment, result);
        }

        /// <summary>
        /// What spending exactly <paramref name="spend"/> delivers to <paramref name="destination"/>
        /// in <paramref name="deliverAsset"/>, along the paths <c>ripple_path_find</c> finds - or,
        /// given <paramref name="books"/>, the local path finder (<see cref="PathFinding"/>).
        /// </summary>
        /// <remarks>
        /// The quote's <see cref="PaymentQuote.Payment"/> carries those paths, the quoted delivery
        /// as its <c>Amount</c> and <paramref name="spend"/> as its <c>SendMax</c>, so against an
        /// unchanged ledger it delivers exactly that; add <c>tfPartialPayment</c> with a
        /// <c>DeliverMin</c> to accept less.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="source">The paying account.</param>
        /// <param name="destination">The receiving account.</param>
        /// <param name="spend">The amount to spend, transfer fees included.</param>
        /// <param name="deliverAsset">The asset to deliver.</param>
        /// <param name="destinationTag">The payment's destination tag, for a destination that requires one.</param>
        /// <param name="books">The order books for a local path search; null to ask the node's <c>ripple_path_find</c>.</param>
        /// <param name="rules">The amendments in force; read from the node when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<PaymentQuote> QuoteSpendAsync(
            this IXrplClient client,
            string source,
            string destination,
            XrplAmount spend,
            IssuedCurrency deliverAsset,
            uint? destinationTag = null,
            IBookIndex books = null,
            LedgerRules rules = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (deliverAsset == null)
                throw new ArgumentNullException(nameof(deliverAsset));

            bool direct = spend.Kind == AmountKind.Xrp && XrplAmount.KindOf(deliverAsset) == AmountKind.Xrp;
            if (direct)
            {
                Payment xrp = Build(source, destination, spend, null, null, 0, destinationTag);
                return new PaymentQuote(xrp, await client.QuotePaymentAsync(xrp, rules, cancellationToken).ConfigureAwait(false));
            }

            // Asked for "as much as possible" (-1), the path finder converts all of SendMax.
            PathFindRequest find = new PathFindRequest
            {
                SourceAccount = source,
                DestinationAccount = destination,
                DestinationAmount = AnyAmount(deliverAsset),
                SendMax = spend,
            };
            List<List<PathStep>> paths = NonEmpty(Matching(await FindAsync(client, find, books, rules, cancellationToken).ConfigureAwait(false), spend.Asset)?.Paths);

            // Quoted with no delivery ceiling, a partial payment delivers what the spend buys.
            Payment quoted = Build(source, destination, Largest(deliverAsset), spend, paths, PaymentFlags.tfPartialPayment, destinationTag);
            PaymentFlowResult result = await client.QuotePaymentAsync(quoted, rules, cancellationToken).ConfigureAwait(false);

            XrplAmount delivered = result.DeliveredAmount ?? XrplAmount.Zero(deliverAsset);
            Payment payment = Build(source, destination, delivered, spend, paths, 0, destinationTag);
            return new PaymentQuote(payment, result);
        }

        /// <summary>
        /// Finds the paths of <paramref name="request"/> locally over the node's ledger, reading
        /// the books from <paramref name="books"/> - or, when null, those between the assets the
        /// source's and the destination's trust lines hold (<see cref="BookIndex.FromAccountsAsync"/>).
        /// </summary>
        /// <param name="client">The node to read from.</param>
        /// <param name="request">What to find paths for.</param>
        /// <param name="books">The order books; see <see cref="BookIndex"/>.</param>
        /// <param name="rules">The amendments in force; read from the node when null.</param>
        /// <param name="cancellationToken">Cancels the search and the reads.</param>
        public static async Task<PathFindResult> FindPathsAsync(
            this IXrplClient client,
            PathFindRequest request,
            IBookIndex books = null,
            LedgerRules rules = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            LOLedger header = await client
                .Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            LedgerIndex at = new LedgerIndex(uint.Parse(((LedgerEntity)header.LedgerEntity).LedgerIndex, CultureInfo.InvariantCulture));
            books ??= await BookIndex
                .FromAccountsAsync(client, new[] { request.SourceAccount, request.DestinationAccount }, request.DomainId, at, cancellationToken)
                .ConfigureAwait(false);
            rules ??= await LedgerRules.FromNodeAsync(client, cancellationToken).ConfigureAwait(false);
            PathfindingSource source = await PathfindingSource.FromNodeAsync(client, books, at, cancellationToken).ConfigureAwait(false);
            return await PathFinding.FindAsync(source, request, rules, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The alternatives of a path search: locally when books are given, from <c>ripple_path_find</c> otherwise.</summary>
        private static async Task<List<(XrplAmount SourceAmount, List<List<PathStep>> Paths)>> FindAsync(
            IXrplClient client,
            PathFindRequest request,
            IBookIndex books,
            LedgerRules rules,
            CancellationToken cancellationToken)
        {
            if (books != null)
            {
                PathFindResult local = await client.FindPathsAsync(request, books, rules, cancellationToken).ConfigureAwait(false);
                return local.Alternatives.Select(a => (a.SourceAmount, a.PathsComputed)).ToList();
            }

            RipplePathFindRequest find = new RipplePathFindRequest(request.SourceAccount, request.DestinationAccount, request.DestinationAmount.ToCurrency())
            {
                SendMax = request.SendMax?.ToCurrency(),
                SourceCurrencies = request.SourceCurrencies.Count == 0 ? null : request.SourceCurrencies.Select(SourceCurrencyOf).ToList(),
                Domain = request.DomainId,
            };
            RipplePathFindResponse response = await client.RipplePathFind(find, cancellationToken).Typed().ConfigureAwait(false);
            return (response?.Alternatives ?? new List<PathAlternative>())
                .Where(a => a?.SourceAmount != null)
                .Select(a => (a.SourceAmount.ToXrplAmount(), a.PathsComputed))
                .ToList();
        }

        /// <summary>The alternative paying in <paramref name="asset"/>'s currency; the first when none matches.</summary>
        private static (XrplAmount SourceAmount, List<List<PathStep>> Paths)? Matching(
            List<(XrplAmount SourceAmount, List<List<PathStep>> Paths)> alternatives,
            IssuedCurrency asset)
        {
            AmountKind kind = XrplAmount.KindOf(asset);
            foreach ((XrplAmount SourceAmount, List<List<PathStep>> Paths) alternative in alternatives)
            {
                IssuedCurrency source = alternative.SourceAmount.Asset;
                if (XrplAmount.KindOf(source) == kind &&
                    (kind == AmountKind.Xrp || string.Equals(source.Currency, asset.Currency, StringComparison.Ordinal)))
                {
                    return alternative;
                }
            }

            return alternatives.Count > 0 ? alternatives[0] : null;
        }

        private static List<List<PathStep>> NonEmpty(List<List<PathStep>> paths) =>
            paths is { Count: > 0 } ? paths : null;

        private static SourceCurrency SourceCurrencyOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp
                ? new SourceCurrency { Currency = "XRP" }
                : new SourceCurrency { Currency = asset.Currency, Issuer = asset.Issuer };

        /// <summary><c>-1</c> of the asset: <c>ripple_path_find</c>'s "as much as SendMax buys".</summary>
        private static XrplAmount AnyAmount(IssuedCurrency asset) => XrplAmount.Parse(asset, "-1");

        /// <summary>The largest amount of the asset a transaction may name.</summary>
        private static XrplAmount Largest(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp
                ? XrplAmount.FromUnits(asset, AmountKind.Xrp, (long)XrplAmount.MaxDrops)
                : XrplAmount.Canonical(asset, AmountKind.Iou, false, XrplAmount.MaxIouMantissa, XrplAmount.MaxIouExponent, NumberRounding.ToNearest);

        private static Payment Build(
            string source,
            string destination,
            XrplAmount amount,
            XrplAmount? sendMax,
            List<List<PathStep>> paths,
            PaymentFlags flags,
            uint? destinationTag)
        {
            return new Payment
            {
                Account = source,
                Destination = destination,
                Amount = amount.ToCurrency(),
                SendMax = sendMax?.ToCurrency(),
                Paths = paths,
                Flags = flags == 0 ? null : flags,
                DestinationTag = destinationTag,
            };
        }
    }

    /// <summary>A payment quoted against the ledger, and what it would do.</summary>
    public sealed class PaymentQuote
    {
        internal PaymentQuote(Payment payment, PaymentFlowResult result)
        {
            Payment = payment;
            Result = result;
        }

        /// <summary>The payment the quote describes - accounts, amounts and paths - to autofill, sign and submit.</summary>
        public Payment Payment { get; }

        /// <summary>What the payment would do: its result code, what is delivered and paid, and every change.</summary>
        public PaymentFlowResult Result { get; }

        /// <summary>What the destination receives; null when the payment would fail.</summary>
        public XrplAmount? Delivered => Result.DeliveredAmount;

        /// <summary>What the source spends, transfer fees included; null when the payment would fail.</summary>
        public XrplAmount? Cost => Result.Paid;
    }
}
