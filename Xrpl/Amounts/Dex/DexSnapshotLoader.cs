using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils.Hashes;

using static Xrpl.Models.Common.Common;

using BookOffer = Xrpl.Models.Transactions.Offer;
using BookOffersResponse = Xrpl.Models.Transactions.BookOffers;

namespace Xrpl.Amounts
{
    /// <summary>How <see cref="DexSnapshot"/>'s <c>FromNodeAsync</c> reads the books.</summary>
    public sealed class DexSnapshotOptions
    {
        /// <summary>
        /// Null to read each book with one <c>book_offers</c> request - the funded offers at its
        /// top, at most 100 from a public node - or the number of offers to read from each book
        /// by walking its directories, unfunded offers included. A book with more offers than
        /// were read is listed in <see cref="DexSnapshot.PartialBooks"/>.
        /// </summary>
        public int? BookDepth { get; init; }
    }

    public sealed partial class DexSnapshot
    {
        /// <summary>The offers asked of <c>book_offers</c>; an admin connection returns that many, a public one 100.</summary>
        private const uint BookLimit = 400;

        /// <summary>The most offers <c>book_offers</c> returns to a public connection (<c>Tuning::kBookOffers</c>).</summary>
        private const int PublicBookLimit = 100;

        /// <summary>The ledger entries one <c>ledger_data</c> page reads while walking a book's directories.</summary>
        private const uint DirectoryPage = 32;

        /// <summary>The offers read in parallel while walking a book.</summary>
        private const int OfferReadConcurrency = 8;

        private const uint CredentialAccepted = 0x00010000;

        /// <summary>
        /// Reads from a node, at one validated ledger, everything an <c>OfferCreate</c> by
        /// <paramref name="account"/> trading <paramref name="takerGets"/> for
        /// <paramref name="takerPays"/> can reach: the book it crosses, the two books of the
        /// bridge through XRP when neither side is XRP, the AMM pools on them, and the accounts
        /// and trust lines the crossing reads.
        /// </summary>
        /// <remarks>
        /// Without <see cref="DexSnapshotOptions.BookDepth"/>, each book is read with one
        /// <c>book_offers</c> request: the offers whose owner holds nothing are left out, except
        /// the account's own, and a book deeper than one page is listed in
        /// <see cref="PartialBooks"/>. The transaction lands in a later ledger, whose state may differ.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="account">The account placing the offer.</param>
        /// <param name="takerPays">The asset the offer asks for.</param>
        /// <param name="takerGets">The asset the offer gives.</param>
        /// <param name="options">How deep to read the books; one page each when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static Task<DexSnapshot> FromNodeAsync(
            IXrplClient client,
            string account,
            IssuedCurrency takerPays,
            IssuedCurrency takerGets,
            DexSnapshotOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            if (takerPays == null)
                throw new ArgumentNullException(nameof(takerPays));
            if (takerGets == null)
                throw new ArgumentNullException(nameof(takerGets));

            return LoadAsync(client, CrossingPlan(account, takerPays, takerGets, null), options, cancellationToken);
        }

        /// <summary>
        /// Reads from a node, at one validated ledger, everything <paramref name="offer"/> can
        /// reach: the books it crosses - its domain's when it names one - the AMM pools on the open
        /// ones, the accounts and trust lines the crossing reads, the offer <c>OfferSequence</c>
        /// cancels, and the domain with the credentials that decide who is in it.
        /// </summary>
        /// <remarks>As the other overload, for the books.</remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="offer">The transaction.</param>
        /// <param name="options">How deep to read the books; one page each when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static Task<DexSnapshot> FromNodeAsync(
            IXrplClient client,
            OfferCreate offer,
            DexSnapshotOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (offer?.Account == null || offer.TakerPays == null || offer.TakerGets == null)
                throw new ArgumentException("An offer needs Account, TakerPays and TakerGets.", nameof(offer));

            LoadPlan plan = CrossingPlan(offer.Account, offer.TakerPays.ToXrplAmount().Asset, offer.TakerGets.ToXrplAmount().Asset, offer.DomainID);
            plan.CancelSequence = offer.OfferSequence;
            return LoadAsync(client, plan, options, cancellationToken);
        }

        /// <summary>
        /// Reads from a node, at one validated ledger, everything <paramref name="payment"/> can
        /// reach: the trust lines along its paths and its default path, the books on them - its
        /// domain's when it names one - with the AMM pools on the open ones, the accounts involved
        /// with the destination's deposit preauthorizations, the credentials it presents, and the
        /// domain with the credentials that decide who is in it.
        /// </summary>
        /// <remarks>
        /// Without <see cref="DexSnapshotOptions.BookDepth"/>, each book is read with one
        /// <c>book_offers</c> request: the offers whose owner holds nothing are left out, except
        /// the sender's own, and a book deeper than one page is listed in <see cref="PartialBooks"/>.
        /// The transaction lands in a later ledger, whose state may differ.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="payment">The payment, with its paths as <c>ripple_path_find</c> returned them.</param>
        /// <param name="options">How deep to read the books; one page each when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static Task<DexSnapshot> FromNodeAsync(
            IXrplClient client,
            Payment payment,
            DexSnapshotOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (payment?.Amount == null || payment.Account == null || payment.Destination == null)
                throw new ArgumentException("A payment needs Account, Destination and Amount.", nameof(payment));

            XrplAmount amount = payment.Amount.ToXrplAmount();
            IssuedCurrency sendMax = payment.SendMax?.ToXrplAmount().Asset ?? (amount.Kind == AmountKind.Xrp
                ? amount.Asset
                : new IssuedCurrency { Currency = amount.Asset.Currency, Issuer = payment.Account });

            LoadPlan plan = new LoadPlan
            {
                Request = new StrandBuilder.Request
                {
                    Source = payment.Account,
                    Destination = payment.Destination,
                    Deliver = amount.Asset,
                    SendMax = sendMax,
                    DomainId = payment.DomainID,
                },
                AddDefaultPath = !(payment.Flags ?? 0).HasFlag(PaymentFlags.tfNoDirectRipple),
                Taker = payment.Account,
                CredentialIds = payment.CredentialIDs,
            };
            foreach (List<PathStep> path in payment.Paths ?? new List<List<PathStep>>())
            {
                List<PathElement> elements = new List<PathElement>();
                foreach (PathStep step in path ?? new List<PathStep>())
                    elements.Add(new PathElement(step.Account, step.CurrencyCode, step.Issuer));
                plan.Paths.Add(elements);
            }

            return LoadAsync(client, plan, options, cancellationToken);
        }

        /// <summary>What to read for one transaction.</summary>
        private sealed class LoadPlan
        {
            internal StrandBuilder.Request Request { get; init; }

            internal List<IReadOnlyList<PathElement>> Paths { get; } = new List<IReadOnlyList<PathElement>>();

            internal bool AddDefaultPath { get; init; }

            /// <summary>The account <c>book_offers</c> reads for, whose own unfunded offers it keeps.</summary>
            internal string Taker { get; init; }

            internal uint? CancelSequence { get; set; }

            internal List<string> CredentialIds { get; init; }

            /// <summary>More requests whose strands the snapshot must cover as well: the same payment from other source assets.</summary>
            internal List<StrandBuilder.Request> ExtraRequests { get; } = new List<StrandBuilder.Request>();

            /// <summary>The ledger to read; the last validated one when null.</summary>
            internal LedgerIndex At { get; init; }
        }

        /// <summary>
        /// The state the payment engine reads for <paramref name="requests"/> along
        /// <paramref name="paths"/> and, when <paramref name="addDefaultPath"/>, the default
        /// path, read at <paramref name="at"/>.
        /// </summary>
        internal static Task<DexSnapshot> ForPathsAsync(
            IXrplClient client,
            IReadOnlyList<StrandBuilder.Request> requests,
            IEnumerable<IReadOnlyList<PathElement>> paths,
            bool addDefaultPath,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            LoadPlan plan = new LoadPlan
            {
                Request = requests[0],
                AddDefaultPath = addDefaultPath,
                Taker = requests[0].Source,
                At = at,
            };
            plan.Paths.AddRange(paths);
            for (int i = 1; i < requests.Count; i++)
                plan.ExtraRequests.Add(requests[i]);

            return LoadAsync(client, plan, null, cancellationToken);
        }

        private static LoadPlan CrossingPlan(string account, IssuedCurrency takerPays, IssuedCurrency takerGets, string domainId)
        {
            LoadPlan plan = new LoadPlan
            {
                Request = new StrandBuilder.Request
                {
                    Source = account,
                    Destination = account,
                    Deliver = takerPays,
                    SendMax = takerGets,
                    OfferCrossing = true,
                    DomainId = domainId,
                },
                AddDefaultPath = true,
                Taker = account,
            };
            if (XrplAmount.KindOf(takerGets) != AmountKind.Xrp && XrplAmount.KindOf(takerPays) != AmountKind.Xrp)
                plan.Paths.Add(new[] { new PathElement(null, "XRP", null) });

            return plan;
        }

        /// <summary>The accounts, lines, books, domain and credentials the planned strands reach, read at one validated ledger.</summary>
        private static async Task<DexSnapshot> LoadAsync(IXrplClient client, LoadPlan plan, DexSnapshotOptions options, CancellationToken cancellationToken)
        {
            StrandBuilder.Request request = plan.Request;
            string domainId = string.IsNullOrEmpty(request.DomainId) ? null : request.DomainId;
            LOLedger header = await client
                .Ledger(new LedgerRequest { LedgerIndex = plan.At ?? new LedgerIndex(LedgerIndexType.Validated) }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            LedgerEntity ledger = header.LedgerEntity as LedgerEntity
                                  ?? throw new InvalidOperationException("The node returned no ledger header.");
            LedgerIndex at = new LedgerIndex(uint.Parse(ledger.LedgerIndex, CultureInfo.InvariantCulture));

            (ulong reserveBase, ulong reserveIncrement) = await ReservesAsync(client, at, cancellationToken).ConfigureAwait(false);

            HashSet<string> accounts = new HashSet<string>(StringComparer.Ordinal) { request.Source, request.Destination };
            HashSet<(string A, string B, string Currency)> lines = new HashSet<(string, string, string)>();
            List<(IssuedCurrency In, IssuedCurrency Out)> books = new List<(IssuedCurrency, IssuedCurrency)>();
            HashSet<BookKey> seenBooks = new HashSet<BookKey>();

            List<IReadOnlyList<PathElement>> planned = new List<IReadOnlyList<PathElement>>(plan.Paths);
            if (plan.AddDefaultPath)
                planned.Insert(0, Array.Empty<PathElement>());

            List<StrandBuilder.Request> routes = new List<StrandBuilder.Request> { request };
            routes.AddRange(plan.ExtraRequests);
            foreach (StrandBuilder.Request route in routes)
            foreach (IReadOnlyList<PathElement> path in planned)
            {
                foreach (StrandBuilder.Hop hop in StrandBuilder.Plan(route, path).Hops)
                {
                    switch (hop.Kind)
                    {
                        case StrandBuilder.HopKind.Direct:
                            accounts.Add(hop.Source);
                            accounts.Add(hop.Destination);
                            AddLine(lines, hop.Source, hop.Destination, hop.Currency);
                            break;
                        case StrandBuilder.HopKind.XrpEndpoint:
                            accounts.Add(hop.Source);
                            break;
                        default:
                            AddIssuer(accounts, hop.In);
                            AddIssuer(accounts, hop.Out);
                            if (seenBooks.Add(BookKey.Of(hop.In, hop.Out, domainId)))
                                books.Add((hop.In, hop.Out));
                            break;
                    }
                }
            }

            List<DexOffer> offers = new List<DexOffer>();
            HashSet<string> offerIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<DexAmmPool> pools = new List<DexAmmPool>();
            List<DexBook> partialBooks = new List<DexBook>();
            HashSet<string> domainOwners = new HashSet<string>(StringComparer.Ordinal);
            void AddOffer(DexOffer offer)
            {
                if (!offerIndexes.Add(offer.Index))
                    return;

                offers.Add(offer);
                accounts.Add(offer.Account);
                if (offer.TakerPays.Kind == AmountKind.Iou)
                    AddLine(lines, offer.Account, offer.TakerPays.Asset.Issuer, offer.TakerPays.Asset.Currency);
                if (offer.TakerGets.Kind == AmountKind.Iou)
                    AddLine(lines, offer.Account, offer.TakerGets.Asset.Issuer, offer.TakerGets.Asset.Currency);
                if (!string.IsNullOrEmpty(offer.DomainId))
                    domainOwners.Add(offer.Account);
            }

            foreach ((IssuedCurrency @in, IssuedCurrency @out) in books)
            {
                (IReadOnlyList<DexOffer> bookOffers, bool partial) = options?.BookDepth is { } depth
                    ? await WalkBookAsync(client, @in, @out, domainId, depth, at, cancellationToken).ConfigureAwait(false)
                    : await BookAsync(client, plan.Taker, @in, @out, domainId, at, cancellationToken).ConfigureAwait(false);
                foreach (DexOffer offer in bookOffers)
                    AddOffer(offer);
                if (partial)
                    partialBooks.Add(new DexBook { TakerPays = @in, TakerGets = @out, DomainId = domainId });

                // A pool serves the open book only.
                if (domainId == null && await PoolAsync(client, @in, @out, at, cancellationToken).ConfigureAwait(false) is { } pool)
                    pools.Add(pool);
            }

            if (plan.CancelSequence is { } cancelSequence &&
                await OfferAsync(client, new LedgerEntryRequest { Offer = new OfferQuery { Account = request.Source, Seq = cancelSequence }, LedgerIndex = at }, cancellationToken)
                    .ConfigureAwait(false) is { } cancelled)
            {
                AddOffer(cancelled);
            }

            List<DexDomain> domains = new List<DexDomain>();
            List<DexCredential> credentials = new List<DexCredential>();
            if (domainId != null && await DomainAsync(client, domainId, at, cancellationToken).ConfigureAwait(false) is { } domain)
            {
                domains.Add(domain);
                HashSet<string> members = new HashSet<string>(domainOwners, StringComparer.Ordinal) { request.Source, request.Destination };
                foreach (string member in members)
                {
                    foreach (DexCredentialType accepted in domain.AcceptedCredentials)
                    {
                        if (await CredentialAsync(client, new CredentialQuery { Subject = member, Issuer = accepted.Issuer, CredentialType = accepted.CredentialType }, at, cancellationToken)
                                .ConfigureAwait(false) is { } credential)
                        {
                            credentials.Add(credential);
                        }
                    }
                }
            }

            foreach (string id in plan.CredentialIds ?? new List<string>())
            {
                if (await CredentialAsync(client, id, at, cancellationToken).ConfigureAwait(false) is { } credential &&
                    !credentials.Exists(c => SameCredential(c, credential)))
                {
                    credentials.Add(credential);
                }
            }

            // A deep book brings an account and up to two lines per owner: read them in parallel, bounded.
            using SemaphoreSlim gate = new SemaphoreSlim(OfferReadConcurrency);
            async Task<T> Gated<T>(Func<Task<T>> read)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await read().ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }

            DexAccount[] accountReads = await Task.WhenAll(accounts
                    .Where(address => !string.IsNullOrEmpty(address))
                    .Select(address => Gated(() => AccountAsync(client, address, at, cancellationToken))))
                .ConfigureAwait(false);
            List<DexAccount> accountStates = accountReads.Where(state => state != null).ToList();

            DexTrustLine[] lineReads = await Task.WhenAll(lines
                    .Select(line => Gated(() => LineAsync(client, line.A, line.B, line.Currency, at, cancellationToken))))
                .ConfigureAwait(false);
            List<DexTrustLine> lineStates = lineReads.Where(line => line != null).ToList();

            return new DexSnapshot
            {
                // The transaction lands in a later ledger, whose parent closed no earlier than this one.
                ParentCloseTime = ledger.CloseTime is { } closed ? (uint)LendingMath.RippleSeconds(closed) : 0,
                ReserveBase = reserveBase,
                ReserveIncrement = reserveIncrement,
                Accounts = accountStates,
                TrustLines = lineStates,
                Offers = offers,
                Pools = pools,
                Domains = domains,
                Credentials = credentials,
                PartialBooks = partialBooks,
            };
        }

        private static bool SameCredential(DexCredential a, DexCredential b) =>
            string.Equals(a.Subject, b.Subject, StringComparison.Ordinal) &&
            string.Equals(a.Issuer, b.Issuer, StringComparison.Ordinal) &&
            string.Equals(a.CredentialType, b.CredentialType, StringComparison.OrdinalIgnoreCase);

        private static void AddIssuer(HashSet<string> accounts, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Iou)
                accounts.Add(asset.Issuer);
        }

        private static void AddLine(HashSet<(string, string, string)> lines, string a, string b, string currency)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.Ordinal))
                return;

            LineKey key = LineKey.Of(a, b, currency);
            lines.Add((key.First, key.Second, key.Currency));
        }

        internal static async Task<(ulong Base, ulong Increment)> ReservesAsync(IXrplClient client, LedgerIndex at, CancellationToken cancellationToken)
        {
            LedgerEntryResponse response = await client
                .LedgerEntry(new LedgerEntryRequest { Index = "4BC50C9B0D8515D3EAAE1E74B29A95804346C491EE1A95BF25E4AAB854A6A651", LedgerIndex = at }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            if (response?.Node is not LOFeeSettings fees)
                throw new InvalidOperationException("The node returned no FeeSettings.");

            if (fees.ReserveBaseDrops != null && fees.ReserveIncrementDrops != null)
                return (ulong.Parse(fees.ReserveBaseDrops, CultureInfo.InvariantCulture), ulong.Parse(fees.ReserveIncrementDrops, CultureInfo.InvariantCulture));

            return (fees.ReserveBase ?? 0, fees.ReserveIncrement ?? 0);
        }

        // ---- books ----

        /// <summary>One <c>book_offers</c> page of the book, and whether the book may hold more.</summary>
        private static async Task<(IReadOnlyList<DexOffer> Offers, bool Partial)> BookAsync(
            IXrplClient client,
            string taker,
            IssuedCurrency @in,
            IssuedCurrency @out,
            string domainId,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            // With the taker named, the account's own unfunded offers are returned too.
            BookOffersRequest request = new BookOffersRequest
            {
                Taker = taker,
                TakerPays = TakerAmountOf(@in),
                TakerGets = TakerAmountOf(@out),
                Limit = BookLimit,
                Domain = domainId,
                LedgerIndex = at,
            };
            BookOffersResponse response = await client.BookOffers(request, cancellationToken).Typed().ConfigureAwait(false);
            List<DexOffer> offers = new List<DexOffer>();
            foreach (BookOffer offer in response?.Offers ?? new List<BookOffer>())
            {
                offers.Add(new DexOffer
                {
                    Index = offer.Index,
                    Account = offer.Account,
                    TakerPays = offer.TakerPays.ToXrplAmount(),
                    TakerGets = offer.TakerGets.ToXrplAmount(),
                    Quality = XrplQuality.FromBookDirectory(offer.BookDirectory),
                    Expiration = offer.Expiration is { } expiration ? (uint)LendingMath.RippleSeconds(expiration) : null,
                    DomainId = string.IsNullOrEmpty(offer.DomainID) ? null : offer.DomainID,
                    Hybrid = offer.Flags.HasFlag(OfferFlags.lsfHybrid),
                });
            }

            // A full public page may have been cut; so may an admin one, which unfunded offers do not fill.
            return (offers, offers.Count >= PublicBookLimit);
        }

        private static TakerAmount TakerAmountOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp
                ? new TakerAmount { Currency = "XRP" }
                : new TakerAmount { Currency = asset.Currency, Issuer = asset.Issuer };

        /// <summary>
        /// The book read the way <c>BookTip</c> walks it: its quality directories in key order,
        /// each directory's pages in order, every offer on them - unfunded ones included - up to
        /// <paramref name="depth"/> offers; the book is partial when more are left.
        /// </summary>
        private static async Task<(IReadOnlyList<DexOffer> Offers, bool Partial)> WalkBookAsync(
            IXrplClient client,
            IssuedCurrency @in,
            IssuedCurrency @out,
            string domainId,
            int depth,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth), "A book depth is a positive number of offers.");

            string bookBase = BookBase(@in, @out, domainId);
            string prefix = bookBase.Substring(0, 48);
            List<(string Directory, string Offer)> entries = new List<(string, string)>();
            bool partial = false;
            string marker = bookBase;
            while (marker != null && !partial)
            {
                LOLedgerData page = await client
                    .LedgerData(new LedgerDataRequest { LedgerIndex = at, Marker = marker, Limit = DirectoryPage, Binary = false }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);

                bool leftBook = false;
                foreach (BaseLedgerEntry entry in page?.State ?? new List<BaseLedgerEntry>())
                {
                    if (!entry.Index.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        leftBook = true;
                        break;
                    }

                    if (entry is not LODirectoryNode root)
                        continue;

                    foreach (string offer in await DirectoryAsync(client, root, at, cancellationToken).ConfigureAwait(false))
                    {
                        if (entries.Count == depth)
                        {
                            partial = true;
                            break;
                        }

                        entries.Add((root.Index, offer));
                    }

                    if (partial)
                        break;
                }

                marker = leftBook ? null : MarkerOf(page);
            }

            DexOffer[] offers = new DexOffer[entries.Count];
            using SemaphoreSlim gate = new SemaphoreSlim(OfferReadConcurrency);
            await Task.WhenAll(entries.Select(async (entry, i) =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    offers[i] = await OfferAsync(client, new LedgerEntryRequest { Index = entry.Offer, LedgerIndex = at }, cancellationToken, entry.Directory)
                        .ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);

            return (offers.Where(offer => offer != null).ToList(), partial);
        }

        /// <summary>The offers of a quality directory, its root page and every page after it.</summary>
        private static async Task<List<string>> DirectoryAsync(IXrplClient client, LODirectoryNode root, LedgerIndex at, CancellationToken cancellationToken)
        {
            List<string> offers = new List<string>(root.Indexes ?? new List<string>());
            string next = root.IndexNext;
            while (!string.IsNullOrEmpty(next) && ulong.Parse(next, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != 0)
            {
                ulong page = ulong.Parse(next, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                LedgerEntryResponse response = await client
                    .LedgerEntry(new LedgerEntryRequest { Directory = new DirectoryQuery { DirRoot = root.Index, SubIndex = checked((uint)page) }, LedgerIndex = at }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                if (response?.Node is not LODirectoryNode node)
                    break;

                offers.AddRange(node.Indexes ?? new List<string>());
                next = node.IndexNext;
            }

            return offers;
        }

        /// <summary>The <c>ledger_data</c> marker to resume from; null at the end of the ledger.</summary>
        private static string MarkerOf(LOLedgerData page) =>
            page?.Marker switch
            {
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                _ => null,
            };

        /// <summary><c>getBookBase</c>: the key of the book's quality-zero directory, every quality directory of the book following it.</summary>
        internal static string BookBase(IssuedCurrency @in, IssuedCurrency @out, string domainId)
        {
            string preimage = LedgerSpace.BookDir.LedgerSpaceHex() +
                              CurrencyHex(@in) + CurrencyHex(@out) +
                              IssuerHex(@in) + IssuerHex(@out) +
                              (string.IsNullOrEmpty(domainId) ? string.Empty : domainId.ToUpperInvariant());
            return preimage.Sha512Half().Substring(0, 48) + "0000000000000000";
        }

        /// <summary>A currency as its 20 bytes: zero for XRP, a three-letter code at bytes 12 to 14.</summary>
        private static string CurrencyHex(IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Xrp)
                return new string('0', 40);
            if (asset.Currency.IsHexCurrencyCode())
                return asset.Currency.ToUpperInvariant();

            string code = string.Concat(asset.Currency.Select(c => ((int)c).ToString("X2", CultureInfo.InvariantCulture)));
            return new string('0', 24) + code + new string('0', 10);
        }

        private static string IssuerHex(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp ? new string('0', 40) : asset.Issuer.AddressToHex().ToUpperInvariant();

        /// <summary>An offer read by <c>ledger_entry</c>; null when it is gone.</summary>
        private static async Task<DexOffer> OfferAsync(IXrplClient client, LedgerEntryRequest request, CancellationToken cancellationToken, string directory = null)
        {
            LOOffer offer;
            try
            {
                LedgerEntryResponse response = await client.LedgerEntry(request, cancellationToken).Typed().ConfigureAwait(false);
                offer = response?.Node as LOOffer;
            }
            catch (RippledException exception) when (exception.Response?.Error == XrplErrorCodes.EntryNotFound)
            {
                return null;
            }

            if (offer?.TakerPays == null || offer.TakerGets == null)
                return null;

            return new DexOffer
            {
                Index = offer.Index ?? request.Index,
                Account = offer.Account,
                TakerPays = offer.TakerPays.ToXrplAmount(),
                TakerGets = offer.TakerGets.ToXrplAmount(),
                Quality = XrplQuality.FromBookDirectory(directory ?? offer.BookDirectory),
                Expiration = offer.Expiration is { } expiration ? (uint)LendingMath.RippleSeconds(expiration) : null,
                DomainId = string.IsNullOrEmpty(offer.DomainID) ? null : offer.DomainID,
                Hybrid = (offer.Flags ?? 0).HasFlag(OfferFlags.lsfHybrid),
            };
        }

        private static async Task<DexAmmPool> PoolAsync(
            IXrplClient client,
            IssuedCurrency asset,
            IssuedCurrency asset2,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            AMMInfo amm;
            try
            {
                AMMInfoResponse response = await client
                    .AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2, LedgerIndex = at }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                amm = response?.Amm;
            }
            catch (RippledException exception) when (exception.Response?.Error is "actNotFound" or XrplErrorCodes.EntryNotFound)
            {
                return null;
            }

            if (amm?.Amount == null || amm.Amount2 == null || amm.LPTokenBalance == null)
                return null;

            XrplAmount lpTokens = amm.LPTokenBalance.ToXrplAmount();
            if (lpTokens.IsZero)
                return null;

            List<string> authorized = new List<string>();
            foreach (AuthAccount entry in amm.AuctionSlot?.AuthAccounts ?? new List<AuthAccount>())
            {
                if (entry?.Account != null)
                    authorized.Add(entry.Account);
            }

            return new DexAmmPool
            {
                Account = amm.Account,
                Balance = amm.Amount.ToXrplAmount(),
                Balance2 = amm.Amount2.ToXrplAmount(),
                AssetFrozen = amm.AssetFrozen == true,
                Asset2Frozen = amm.Asset2Frozen == true,
                TradingFee = checked((ushort)amm.TradingFee),
                DiscountedFee = checked((ushort)(amm.AuctionSlot?.DiscountedFee ?? 0)),
                SlotAccount = amm.AuctionSlot?.Account,
                SlotAuthAccounts = authorized,
                SlotExpiration = amm.AuctionSlot?.Expiration is { } expiration ? (uint)LendingMath.RippleSeconds(expiration) : null,
            };
        }

        // ---- domains and credentials ----

        private static async Task<DexDomain> DomainAsync(IXrplClient client, string domainId, LedgerIndex at, CancellationToken cancellationToken)
        {
            LOPermissionedDomain domain;
            try
            {
                LedgerEntryResponse response = await client
                    .LedgerEntry(new LedgerEntryRequest { Index = domainId, LedgerIndex = at }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                domain = response?.Node as LOPermissionedDomain;
            }
            catch (RippledException exception) when (exception.Response?.Error == XrplErrorCodes.EntryNotFound)
            {
                return null;
            }

            if (domain == null)
                return null;

            List<DexCredentialType> accepted = new List<DexCredentialType>();
            foreach (AcceptedCredentialWrapper wrapper in domain.AcceptedCredentials ?? new List<AcceptedCredentialWrapper>())
            {
                if (wrapper?.Credential?.Issuer != null && wrapper.Credential.CredentialType != null)
                    accepted.Add(new DexCredentialType { Issuer = wrapper.Credential.Issuer, CredentialType = wrapper.Credential.CredentialType });
            }

            return new DexDomain { DomainId = domainId, Owner = domain.Owner, AcceptedCredentials = accepted };
        }

        private static Task<DexCredential> CredentialAsync(IXrplClient client, CredentialQuery query, LedgerIndex at, CancellationToken cancellationToken) =>
            CredentialAsync(client, new LedgerEntryRequest { Credential = query, LedgerIndex = at }, cancellationToken);

        private static Task<DexCredential> CredentialAsync(IXrplClient client, string index, LedgerIndex at, CancellationToken cancellationToken) =>
            CredentialAsync(client, new LedgerEntryRequest { Index = index, LedgerIndex = at }, cancellationToken);

        private static async Task<DexCredential> CredentialAsync(IXrplClient client, LedgerEntryRequest request, CancellationToken cancellationToken)
        {
            LOCredential credential;
            try
            {
                LedgerEntryResponse response = await client.LedgerEntry(request, cancellationToken).Typed().ConfigureAwait(false);
                credential = response?.Node as LOCredential;
            }
            catch (RippledException exception) when (exception.Response?.Error == XrplErrorCodes.EntryNotFound)
            {
                return null;
            }

            if (credential?.Subject == null || credential.Issuer == null || credential.CredentialType == null)
                return null;

            return new DexCredential
            {
                Subject = credential.Subject,
                Issuer = credential.Issuer,
                CredentialType = credential.CredentialType,
                Accepted = ((credential.Flags ?? 0) & CredentialAccepted) != 0,
                Expiration = credential.Expiration is { } expiration ? (uint)LendingMath.RippleSeconds(expiration) : null,
            };
        }

        // ---- accounts and lines ----

        internal static async Task<DexAccount> AccountAsync(IXrplClient client, string address, LedgerIndex at, CancellationToken cancellationToken)
        {
            LOAccountRoot root;
            try
            {
                AccountInfo info = await client
                    .AccountInfo(new AccountInfoRequest(address) { LedgerIndex = at }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                root = info?.AccountData;
            }
            catch (RippledException exception) when (exception.Response?.Error == "actNotFound")
            {
                return null;
            }

            if (root == null)
                return null;

            AccountRootFlags flags = root.Flags ?? 0;
            (List<string> preauthorized, List<IReadOnlyList<DexCredentialType>> preauthorizedCredentials) = flags.HasFlag(AccountRootFlags.lsfDepositAuth)
                ? await PreauthorizedAsync(client, address, at, cancellationToken).ConfigureAwait(false)
                : (new List<string>(), new List<IReadOnlyList<DexCredentialType>>());

            return new DexAccount
            {
                Address = address,
                Balance = ulong.Parse(root.Balance.Value, CultureInfo.InvariantCulture),
                OwnerCount = root.OwnerCount ?? 0,
                Sequence = root.Sequence ?? 0,
                TransferRate = root.TransferRate ?? 0,
                TickSize = root.TickSize ?? 0,
                GlobalFreeze = flags.HasFlag(AccountRootFlags.lsfGlobalFreeze),
                RequireAuth = flags.HasFlag(AccountRootFlags.lsfRequireAuth),
                RequireDestinationTag = flags.HasFlag(AccountRootFlags.lsfRequireDestTag),
                DepositAuth = flags.HasFlag(AccountRootFlags.lsfDepositAuth),
                DefaultRipple = flags.HasFlag(AccountRootFlags.lsfDefaultRipple),
                DisallowIncomingTrustline = flags.HasFlag(AccountRootFlags.lsfDisallowIncomingTrustline),
                DepositPreauthorized = preauthorized,
                DepositPreauthorizedCredentials = preauthorizedCredentials,
            };
        }

        /// <summary>The accounts and the sets of credentials <paramref name="address"/> preauthorized, read from its <c>DepositPreauth</c> objects.</summary>
        private static async Task<(List<string> Accounts, List<IReadOnlyList<DexCredentialType>> Credentials)> PreauthorizedAsync(
            IXrplClient client,
            string address,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            List<string> accounts = new List<string>();
            List<IReadOnlyList<DexCredentialType>> credentials = new List<IReadOnlyList<DexCredentialType>>();
            AccountObjects objects = await client
                .AccountObjects(new AccountObjectsRequest(address) { Type = LedgerEntryType.DepositPreauth, LedgerIndex = at }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            foreach (BaseLedgerEntry entry in objects?.AccountObjectList ?? new List<BaseLedgerEntry>())
            {
                if (entry is not LODepositPreauth preauth)
                    continue;

                if (preauth.Authorize != null)
                    accounts.Add(preauth.Authorize);

                if (preauth.AuthorizeCredentials is { Count: > 0 } set)
                {
                    credentials.Add(set
                        .Where(item => item?.Credential?.Issuer != null && item.Credential.CredentialType != null)
                        .Select(item => new DexCredentialType { Issuer = item.Credential.Issuer, CredentialType = item.Credential.CredentialType })
                        .ToList());
                }
            }

            return (accounts, credentials);
        }

        /// <summary>The trust line between <paramref name="account"/> and <paramref name="peer"/>, from <paramref name="account"/>'s side.</summary>
        internal static async Task<DexTrustLine> LineAsync(
            IXrplClient client,
            string account,
            string peer,
            string currency,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            LORippleState line;
            try
            {
                LedgerEntryResponse response = await client
                    .LedgerEntry(
                        new LedgerEntryRequest
                        {
                            RippleState = new RippleStateQuery { Addresses = new[] { account, peer }, Currency = currency },
                            LedgerIndex = at,
                        },
                        cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                line = response?.Node as LORippleState;
            }
            catch (RippledException exception) when (exception.Response?.Error == XrplErrorCodes.EntryNotFound)
            {
                return null;
            }

            if (line == null)
                return null;

            IssuedCurrency asset = new IssuedCurrency { Currency = currency, Issuer = peer };
            bool accountIsLow = string.Equals(line.LowLimit?.Issuer, account, StringComparison.Ordinal);
            XrplAmount balance = XrplAmount.FromNumber(asset, line.Balance.ToXrplAmount().Value);
            XrplAmount lowLimit = XrplAmount.FromNumber(asset, line.LowLimit.ToXrplAmount().Value);
            XrplAmount highLimit = XrplAmount.FromNumber(asset, line.HighLimit.ToXrplAmount().Value);
            RippleStateFlags flags = line.Flags ?? 0;
            bool Low(RippleStateFlags low, RippleStateFlags high) => flags.HasFlag(accountIsLow ? low : high);
            bool High(RippleStateFlags low, RippleStateFlags high) => flags.HasFlag(accountIsLow ? high : low);
            return new DexTrustLine
            {
                Account = account,
                Balance = accountIsLow ? balance : -balance,
                Limit = accountIsLow ? lowLimit : highLimit,
                PeerLimit = accountIsLow ? highLimit : lowLimit,
                QualityIn = (accountIsLow ? line.LowQualityIn : line.HighQualityIn) ?? 0,
                QualityOut = (accountIsLow ? line.LowQualityOut : line.HighQualityOut) ?? 0,
                PeerQualityIn = (accountIsLow ? line.HighQualityIn : line.LowQualityIn) ?? 0,
                PeerQualityOut = (accountIsLow ? line.HighQualityOut : line.LowQualityOut) ?? 0,
                NoRipple = Low(RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple),
                PeerNoRipple = High(RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple),
                FrozenByAccount = Low(RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze),
                Frozen = High(RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze),
                DeepFrozen = flags.HasFlag(RippleStateFlags.lsfLowDeepFreeze) || flags.HasFlag(RippleStateFlags.lsfHighDeepFreeze),
                Authorized = Low(RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth),
                PeerAuthorized = High(RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth),
                Reserve = Low(RippleStateFlags.lsfLowReserve, RippleStateFlags.lsfHighReserve),
                PeerReserve = High(RippleStateFlags.lsfLowReserve, RippleStateFlags.lsfHighReserve),
            };
        }
    }
}
