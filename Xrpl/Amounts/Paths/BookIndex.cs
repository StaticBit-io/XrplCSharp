using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

using BookOffersResponse = Xrpl.Models.Transactions.BookOffers;

namespace Xrpl.Amounts
{
    /// <summary>
    /// Which order books exist, as the path finder asks: the books an asset can be sold into.
    /// rippled keeps this index (<c>OrderBookDB</c>) for the whole ledger; a client supplies it.
    /// </summary>
    public interface IBookIndex
    {
        /// <summary>
        /// The assets of the books whose offers take <paramref name="takerPays"/>: the books a
        /// holder of that asset can sell it into.
        /// </summary>
        /// <param name="takerPays">The asset sold into the books.</param>
        /// <param name="domainId">The permissioned domain whose books to list; null for the open ones.</param>
        IReadOnlyCollection<IssuedCurrency> BooksFrom(IssuedCurrency takerPays, string domainId);

        /// <summary>Whether a book sells <paramref name="takerPays"/> for XRP.</summary>
        bool HasBookToXrp(IssuedCurrency takerPays, string domainId);
    }

    /// <summary>
    /// A set of order books - each a pair of assets, in the open book or a domain - built from a
    /// whole ledger, from a list of assets, or from the trust lines of the accounts involved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FromLedgerAsync"/> repeats <c>OrderBookDB</c>: every book directory and every AMM
    /// pool of a ledger, which is what the node's path finder searches. It reads the whole ledger,
    /// which suits a private node, a standalone one or a test network. <see cref="Observe"/> adds
    /// the books later transactions create, as the node does between its full rebuilds.
    /// </para>
    /// <para>
    /// <see cref="FromAssetsAsync"/> checks every pair of the assets given, and
    /// <see cref="FromAccountsAsync"/> the assets of the accounts' trust lines: cheap on mainnet,
    /// but a path through a book they leave out is not found.
    /// </para>
    /// </remarks>
    public sealed class BookIndex : IBookIndex
    {
        private const uint LedgerDataPage = 256;
        private const int ProbeConcurrency = 8;

        /// <summary>The most assets <see cref="FromAssetsAsync"/> checks: the pairs grow with the square of them.</summary>
        public const int MaxProbeAssets = 50;

        private static readonly IssuedCurrency XrpAsset = new IssuedCurrency { Currency = "XRP" };

        private readonly Dictionary<(AssetId In, string Domain), List<IssuedCurrency>> _books =
            new Dictionary<(AssetId In, string Domain), List<IssuedCurrency>>();

        private readonly HashSet<(AssetId In, AssetId Out, string Domain)> _known = new HashSet<(AssetId, AssetId, string)>();

        /// <summary>The number of books in the index.</summary>
        public int Count => _known.Count;

        /// <inheritdoc />
        public IReadOnlyCollection<IssuedCurrency> BooksFrom(IssuedCurrency takerPays, string domainId) =>
            _books.TryGetValue((AssetId.Of(takerPays), DomainKey(domainId)), out List<IssuedCurrency> books)
                ? books
                : (IReadOnlyCollection<IssuedCurrency>)Array.Empty<IssuedCurrency>();

        /// <inheritdoc />
        public bool HasBookToXrp(IssuedCurrency takerPays, string domainId) =>
            _known.Contains((AssetId.Of(takerPays), AssetId.Xrp, DomainKey(domainId)));

        /// <summary>Adds a book: offers that take <paramref name="takerPays"/> and give <paramref name="takerGets"/>.</summary>
        /// <param name="takerPays">What the book's offers ask for.</param>
        /// <param name="takerGets">What they give.</param>
        /// <param name="domainId">The permissioned domain of the book; null for the open book.</param>
        public void Add(IssuedCurrency takerPays, IssuedCurrency takerGets, string domainId = null)
        {
            if (takerPays == null)
                throw new ArgumentNullException(nameof(takerPays));
            if (takerGets == null)
                throw new ArgumentNullException(nameof(takerGets));

            AssetId @in = AssetId.Of(takerPays);
            AssetId @out = AssetId.Of(takerGets);
            string domain = DomainKey(domainId);
            if (!_known.Add((@in, @out, domain)))
                return;

            if (!_books.TryGetValue((@in, domain), out List<IssuedCurrency> list))
                _books[(@in, domain)] = list = new List<IssuedCurrency>();

            list.Add(@out.ToAsset());
        }

        /// <summary>Adds a pool: an AMM trades both ways between its two assets, in the open book only.</summary>
        public void AddPool(IssuedCurrency asset, IssuedCurrency asset2)
        {
            Add(asset, asset2);
            Add(asset2, asset);
        }

        /// <summary>
        /// Adds the books a transaction created - a book directory's first offer, a new AMM - as
        /// the node adds them to its index between full rebuilds. Books are never removed.
        /// </summary>
        /// <param name="meta">The transaction's metadata.</param>
        public void Observe(Meta meta)
        {
            foreach (AffectedNode node in meta?.AffectedNodes ?? new List<AffectedNode>())
            {
                if (node.CreatedNode == null)
                    continue;

                if (node.CreatedNode.TryGetNew(out LODirectoryNode directory) && directory != null)
                    AddDirectory(directory, node.CreatedNode.LedgerIndex);
                else if (node.CreatedNode.TryGetNew(out LOAmm amm) && amm?.Asset != null && amm.Asset2 != null)
                    AddPool(amm.Asset.ToXrplAmount().Asset, amm.Asset2.ToXrplAmount().Asset);
            }
        }

        /// <summary>The books of <paramref name="snapshot"/>: its offers' books, hybrid offers in both, and its pools both ways.</summary>
        public static BookIndex FromSnapshot(DexSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            BookIndex index = new BookIndex();
            foreach (DexOffer offer in snapshot.Offers ?? Array.Empty<DexOffer>())
            {
                if (offer == null)
                    continue;
                if (string.IsNullOrEmpty(offer.DomainId) || offer.Hybrid)
                    index.Add(offer.TakerPays.Asset, offer.TakerGets.Asset);
                if (!string.IsNullOrEmpty(offer.DomainId))
                    index.Add(offer.TakerPays.Asset, offer.TakerGets.Asset, offer.DomainId);
            }

            foreach (DexAmmPool pool in snapshot.Pools ?? Array.Empty<DexAmmPool>())
            {
                if (pool != null)
                    index.AddPool(pool.Balance.Asset, pool.Balance2.Asset);
            }

            return index;
        }

        /// <summary>
        /// Every book directory and AMM pool of a ledger, as rippled's <c>OrderBookDB</c> builds
        /// its index: read with <c>ledger_data</c>, the whole ledger page by page.
        /// </summary>
        /// <param name="client">The node to read from.</param>
        /// <param name="ledger">The ledger to read; the last validated one when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<BookIndex> FromLedgerAsync(IXrplClient client, LedgerIndex ledger = null, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            LedgerIndex at = await PinAsync(client, ledger, cancellationToken).ConfigureAwait(false);
            BookIndex index = new BookIndex();
            await ScanAsync(client, at, LedgerEntryType.DirectoryNode, entry =>
            {
                if (entry is LODirectoryNode directory)
                    index.AddDirectory(directory, directory.Index);
            }, cancellationToken).ConfigureAwait(false);
            await ScanAsync(client, at, LedgerEntryType.AMM, entry =>
            {
                if (entry is LOAmm amm && amm.Asset != null && amm.Asset2 != null)
                    index.AddPool(amm.Asset.ToXrplAmount().Asset, amm.Asset2.ToXrplAmount().Asset);
            }, cancellationToken).ConfigureAwait(false);
            return index;
        }

        /// <summary>
        /// The books between the assets given, XRP included: each ordered pair checked for an
        /// offer with <c>book_offers</c>, and for a pool with <c>amm_info</c>.
        /// </summary>
        /// <remarks>
        /// <c>book_offers</c> leaves out offers whose owner holds nothing, so a book of only such
        /// offers is not found. The pairs grow with the square of the assets, so at most
        /// <see cref="MaxProbeAssets"/> assets are accepted, XRP included.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="assets">The assets; XRP is added.</param>
        /// <param name="domainId">A permissioned domain whose books to check too; null for the open books only.</param>
        /// <param name="ledger">The ledger to read; the last validated one when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <exception cref="ArgumentException">More than <see cref="MaxProbeAssets"/> distinct assets.</exception>
        public static async Task<BookIndex> FromAssetsAsync(
            IXrplClient client,
            IEnumerable<IssuedCurrency> assets,
            string domainId = null,
            LedgerIndex ledger = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (assets == null)
                throw new ArgumentNullException(nameof(assets));

            List<IssuedCurrency> distinct = new List<IssuedCurrency> { XrpAsset };
            HashSet<AssetId> seen = new HashSet<AssetId> { AssetId.Xrp };
            foreach (IssuedCurrency asset in assets)
            {
                if (asset != null && seen.Add(AssetId.Of(asset)))
                    distinct.Add(AssetId.Of(asset).ToAsset());
            }

            if (distinct.Count > MaxProbeAssets)
            {
                throw new ArgumentException(
                    $"{distinct.Count} assets, XRP included, exceed the {MaxProbeAssets} whose pairs are checked; " +
                    "pass fewer assets, or read the books with FromLedgerAsync.",
                    nameof(assets));
            }

            LedgerIndex at = await PinAsync(client, ledger, cancellationToken).ConfigureAwait(false);

            // (in, out, domain) for a book_offers probe; a null domain with pool set for an amm_info probe.
            List<(IssuedCurrency In, IssuedCurrency Out, string Domain, bool Pool)> probes = new List<(IssuedCurrency, IssuedCurrency, string, bool)>();
            for (int i = 0; i < distinct.Count; i++)
            {
                for (int j = 0; j < distinct.Count; j++)
                {
                    if (i == j)
                        continue;
                    probes.Add((distinct[i], distinct[j], null, false));
                    if (!string.IsNullOrEmpty(domainId))
                        probes.Add((distinct[i], distinct[j], domainId, false));
                    if (i < j)
                        probes.Add((distinct[i], distinct[j], null, true));
                }
            }

            bool[] found = new bool[probes.Count];
            int next = -1;
            async Task WorkAsync()
            {
                for (int k = Interlocked.Increment(ref next); k < probes.Count; k = Interlocked.Increment(ref next))
                {
                    (IssuedCurrency @in, IssuedCurrency @out, string domain, bool pool) = probes[k];
                    found[k] = pool
                        ? await HasPoolAsync(client, @in, @out, at, cancellationToken).ConfigureAwait(false)
                        : await HasOfferAsync(client, @in, @out, domain, at, cancellationToken).ConfigureAwait(false);
                }
            }

            await Task.WhenAll(Enumerable.Range(0, Math.Min(ProbeConcurrency, probes.Count)).Select(_ => WorkAsync())).ConfigureAwait(false);

            BookIndex index = new BookIndex();
            for (int k = 0; k < probes.Count; k++)
            {
                if (!found[k])
                    continue;
                if (probes[k].Pool)
                    index.AddPool(probes[k].In, probes[k].Out);
                else
                    index.Add(probes[k].In, probes[k].Out, probes[k].Domain);
            }

            return index;
        }

        /// <summary>
        /// The books between the assets the accounts' trust lines hold or issue, XRP included,
        /// found as <see cref="FromAssetsAsync"/> finds them: a guess at the books a payment
        /// between these accounts can use.
        /// </summary>
        /// <remarks>
        /// A line gives the peer's currency when the account holds some or trusts the peer for
        /// it, and the account's own currency when the account owes some or the peer trusts it;
        /// so a gateway's lines to its holders give its own currencies once each. More than
        /// <see cref="MaxProbeAssets"/> assets fail as in <see cref="FromAssetsAsync"/>.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="accounts">The accounts, usually the payment's source and destination.</param>
        /// <param name="domainId">A permissioned domain whose books to check too; null for the open books only.</param>
        /// <param name="ledger">The ledger to read; the last validated one when null.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static async Task<BookIndex> FromAccountsAsync(
            IXrplClient client,
            IEnumerable<string> accounts,
            string domainId = null,
            LedgerIndex ledger = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (accounts == null)
                throw new ArgumentNullException(nameof(accounts));

            LedgerIndex at = await PinAsync(client, ledger, cancellationToken).ConfigureAwait(false);
            List<IssuedCurrency> assets = new List<IssuedCurrency>();
            foreach (string account in accounts.Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.Ordinal))
            {
                foreach (DexTrustLine line in await PathfindingSource.AccountLinesAsync(client, account, at, cancellationToken).ConfigureAwait(false))
                {
                    // What the account holds or may receive is the peer's currency; what it owes or may issue, its own.
                    if (StepMath.IsPositive(line.Balance) || line.Limit is { IsZero: false })
                        assets.Add(line.Balance.Asset);
                    if (line.Balance.IsNegative || line.PeerLimit is { IsZero: false })
                        assets.Add(new IssuedCurrency { Currency = line.Balance.Asset.Currency, Issuer = account });
                }
            }

            return await FromAssetsAsync(client, assets, domainId, at, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>A book directory's book, when the entry is the first page of one (<c>OrderBookDB::update</c>).</summary>
        private void AddDirectory(LODirectoryNode directory, string index)
        {
            if (string.IsNullOrEmpty(directory.ExchangeRate) || directory.TakerPaysCurrency == null || directory.TakerGetsCurrency == null)
                return;
            if (directory.RootIndex != null && index != null && !string.Equals(directory.RootIndex, index, StringComparison.OrdinalIgnoreCase))
                return;

            Add(
                AssetFromHex(directory.TakerPaysCurrency, directory.TakerPaysIssuer),
                AssetFromHex(directory.TakerGetsCurrency, directory.TakerGetsIssuer),
                string.IsNullOrEmpty(directory.DomainID) ? null : directory.DomainID);
        }

        /// <summary>An asset as a directory stores it: a 160-bit currency and a 160-bit account, both hex.</summary>
        private static IssuedCurrency AssetFromHex(string currency, string issuer)
        {
            string code = PathCurrency.Normalize(currency);
            if (code == "XRP")
                return XrpAsset;

            string address = issuer != null && issuer.Length == 40
                ? XrplCodec.EncodeAccountID(HexToBytes(issuer))
                : issuer;
            return new IssuedCurrency { Currency = code, Issuer = address };
        }

        private static byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        private static string DomainKey(string domainId) =>
            string.IsNullOrEmpty(domainId) ? string.Empty : domainId.ToUpperInvariant();

        private static async Task<LedgerIndex> PinAsync(IXrplClient client, LedgerIndex ledger, CancellationToken cancellationToken)
        {
            LOLedger header = await client
                .Ledger(new LedgerRequest { LedgerIndex = ledger ?? new LedgerIndex(LedgerIndexType.Validated) }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            LedgerEntity entity = header.LedgerEntity as LedgerEntity
                                  ?? throw new InvalidOperationException("The node returned no ledger header.");
            return new LedgerIndex(uint.Parse(entity.LedgerIndex, CultureInfo.InvariantCulture));
        }

        private static async Task ScanAsync(IXrplClient client, LedgerIndex at, LedgerEntryType type, Action<BaseLedgerEntry> visit, CancellationToken cancellationToken)
        {
            object marker = null;
            do
            {
                LOLedgerData page = await client
                    .LedgerData(new LedgerDataRequest { LedgerIndex = at, Marker = marker, Limit = LedgerDataPage, Binary = false, LedgerEntryType = type }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                foreach (BaseLedgerEntry entry in page?.State ?? new List<BaseLedgerEntry>())
                    visit(entry);

                marker = page?.Marker switch
                {
                    string text => text,
                    JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                    _ => null,
                };
            }
            while (marker != null);
        }

        private static async Task<bool> HasOfferAsync(IXrplClient client, IssuedCurrency @in, IssuedCurrency @out, string domainId, LedgerIndex at, CancellationToken cancellationToken)
        {
            BookOffersRequest request = new BookOffersRequest
            {
                TakerPays = TakerAmountOf(@in),
                TakerGets = TakerAmountOf(@out),
                Limit = 1,
                Domain = domainId,
                LedgerIndex = at,
            };
            try
            {
                BookOffersResponse response = await client.BookOffers(request, cancellationToken).Typed().ConfigureAwait(false);
                return response?.Offers is { Count: > 0 };
            }
            catch (RippledException)
            {
                return false;
            }
        }

        private static async Task<bool> HasPoolAsync(IXrplClient client, IssuedCurrency asset, IssuedCurrency asset2, LedgerIndex at, CancellationToken cancellationToken)
        {
            try
            {
                AMMInfoResponse response = await client
                    .AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2, LedgerIndex = at }, cancellationToken)
                    .Typed()
                    .ConfigureAwait(false);
                return response?.Amm?.Account != null;
            }
            catch (RippledException)
            {
                return false;
            }
        }

        private static TakerAmount TakerAmountOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp
                ? new TakerAmount { Currency = "XRP" }
                : new TakerAmount { Currency = asset.Currency, Issuer = asset.Issuer };
    }

    /// <summary>An asset as a key: the normalized currency and the issuer, XRP with an empty one.</summary>
    internal readonly record struct AssetId(string Currency, string Issuer)
    {
        internal static readonly AssetId Xrp = new AssetId("XRP", string.Empty);

        internal static AssetId Of(IssuedCurrency asset)
        {
            string currency = PathCurrency.Normalize(asset?.Currency);
            return currency == "XRP" ? Xrp : new AssetId(currency, asset.Issuer ?? string.Empty);
        }

        internal bool IsXrp => Currency == "XRP";

        internal IssuedCurrency ToAsset() =>
            IsXrp ? new IssuedCurrency { Currency = "XRP" } : new IssuedCurrency { Currency = Currency, Issuer = Issuer };
    }

    /// <summary>Currency codes as the ledger compares them: a three-letter code and its 160-bit form are the same currency.</summary>
    internal static class PathCurrency
    {
        /// <summary>
        /// The canonical form: <c>XRP</c> for XRP, the three-letter code for a standard currency
        /// in either form, and the upper-case 40-digit hex otherwise.
        /// </summary>
        internal static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code) || code == "XRP")
                return "XRP";
            if (code.Length != 40)
                return code;

            string hex = code.ToUpperInvariant();
            if (hex.Trim('0').Length == 0)
                return "XRP";

            // Standard currencies: twelve zero bytes, three ASCII letters, five zero bytes.
            if (hex.StartsWith("000000000000000000000000", StringComparison.Ordinal) && hex.EndsWith("0000000000", StringComparison.Ordinal))
            {
                char[] letters = new char[3];
                bool printable = true;
                for (int i = 0; i < 3; i++)
                {
                    int value = int.Parse(hex.Substring(24 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    printable &= value > 0x20 && value < 0x7F;
                    letters[i] = (char)value;
                }

                string standard = new string(letters);
                if (printable && standard != "XRP")
                    return standard;
            }

            return hex;
        }
    }
}
