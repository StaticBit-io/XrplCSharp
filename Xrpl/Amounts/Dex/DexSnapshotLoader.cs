using System;
using System.Collections.Generic;
using System.Globalization;
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

using static Xrpl.Models.Common.Common;

using BookOffer = Xrpl.Models.Transactions.Offer;
using BookOffersResponse = Xrpl.Models.Transactions.BookOffers;

namespace Xrpl.Amounts
{
    public sealed partial class DexSnapshot
    {
        /// <summary>The most offers <c>book_offers</c> returns for one book; its marker does not page further.</summary>
        private const uint BookLimit = 400;

        /// <summary>
        /// Reads from a node, at one validated ledger, everything an <c>OfferCreate</c> by
        /// <paramref name="account"/> trading <paramref name="takerGets"/> for
        /// <paramref name="takerPays"/> can reach: the book it crosses, the two books of the
        /// bridge through XRP when neither side is XRP, the AMM pools on them, and the accounts
        /// and trust lines the crossing reads.
        /// </summary>
        /// <remarks>
        /// Each book is read with one <c>book_offers</c> call, so only its first
        /// <see cref="BookLimit"/> offers are included; a crossing deeper than that is not
        /// represented. <c>book_offers</c> leaves out offers whose owner holds nothing, except the
        /// account's own: the node removes such offers on the way, and the crossing does not list
        /// them. The transaction lands in a later ledger, whose state may differ.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="account">The account placing the offer.</param>
        /// <param name="takerPays">The asset the offer asks for.</param>
        /// <param name="takerGets">The asset the offer gives.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static Task<DexSnapshot> FromNodeAsync(
            IXrplClient client,
            string account,
            IssuedCurrency takerPays,
            IssuedCurrency takerGets,
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

            StrandBuilder.Request request = new StrandBuilder.Request
            {
                Source = account,
                Destination = account,
                Deliver = takerPays,
                SendMax = takerGets,
                OfferCrossing = true,
            };
            List<IReadOnlyList<PathElement>> paths = new List<IReadOnlyList<PathElement>>();
            if (XrplAmount.KindOf(takerGets) != AmountKind.Xrp && XrplAmount.KindOf(takerPays) != AmountKind.Xrp)
                paths.Add(new[] { new PathElement(null, "XRP", null) });

            return LoadAsync(client, request, paths, addDefaultPath: true, account, cancellationToken);
        }

        /// <summary>
        /// Reads from a node, at one validated ledger, everything <paramref name="payment"/> can
        /// reach: the trust lines along its paths and its default path, the books and AMM pools
        /// on them, and the accounts involved - with the destination's deposit preauthorizations.
        /// </summary>
        /// <remarks>
        /// Each book is read with one <c>book_offers</c> call, so only its first
        /// <see cref="BookLimit"/> offers are included, and offers whose owner holds nothing are
        /// left out, except the sender's own. The transaction lands in a later ledger, whose state
        /// may differ.
        /// </remarks>
        /// <param name="client">The node to read from.</param>
        /// <param name="payment">The payment, with its paths as <c>ripple_path_find</c> returned them.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        public static Task<DexSnapshot> FromNodeAsync(IXrplClient client, Payment payment, CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (payment?.Amount == null || payment.Account == null || payment.Destination == null)
                throw new ArgumentException("A payment needs Account, Destination and Amount.", nameof(payment));

            XrplAmount amount = payment.Amount.ToXrplAmount();
            IssuedCurrency sendMax = payment.SendMax?.ToXrplAmount().Asset ?? (amount.Kind == AmountKind.Xrp
                ? amount.Asset
                : new IssuedCurrency { Currency = amount.Asset.Currency, Issuer = payment.Account });

            StrandBuilder.Request request = new StrandBuilder.Request
            {
                Source = payment.Account,
                Destination = payment.Destination,
                Deliver = amount.Asset,
                SendMax = sendMax,
            };
            List<IReadOnlyList<PathElement>> paths = new List<IReadOnlyList<PathElement>>();
            foreach (List<PathStep> path in payment.Paths ?? new List<List<PathStep>>())
            {
                List<PathElement> elements = new List<PathElement>();
                foreach (PathStep step in path ?? new List<PathStep>())
                    elements.Add(new PathElement(step.Account, step.CurrencyCode, step.Issuer));
                paths.Add(elements);
            }

            bool addDefaultPath = !((payment.Flags ?? 0).HasFlag(PaymentFlags.tfNoDirectRipple));
            return LoadAsync(client, request, paths, addDefaultPath, payment.Account, cancellationToken);
        }

        /// <summary>The accounts, lines and books the planned strands reach, read at one validated ledger.</summary>
        private static async Task<DexSnapshot> LoadAsync(
            IXrplClient client,
            StrandBuilder.Request request,
            List<IReadOnlyList<PathElement>> paths,
            bool addDefaultPath,
            string taker,
            CancellationToken cancellationToken)
        {
            LOLedger header = await client
                .Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }, cancellationToken)
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

            List<IReadOnlyList<PathElement>> planned = new List<IReadOnlyList<PathElement>>(paths);
            if (addDefaultPath)
                planned.Insert(0, Array.Empty<PathElement>());

            foreach (IReadOnlyList<PathElement> path in planned)
            {
                foreach (StrandBuilder.Hop hop in StrandBuilder.Plan(request, path).Hops)
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
                            if (seenBooks.Add(BookKey.Of(hop.In, hop.Out)))
                                books.Add((hop.In, hop.Out));
                            break;
                    }
                }
            }

            List<DexOffer> offers = new List<DexOffer>();
            List<DexAmmPool> pools = new List<DexAmmPool>();
            foreach ((IssuedCurrency @in, IssuedCurrency @out) in books)
            {
                foreach (BookOffer offer in await BookAsync(client, taker, @in, @out, at, cancellationToken).ConfigureAwait(false))
                {
                    XrplAmount pays = offer.TakerPays.ToXrplAmount();
                    XrplAmount gets = offer.TakerGets.ToXrplAmount();
                    offers.Add(new DexOffer
                    {
                        Index = offer.Index,
                        Account = offer.Account,
                        TakerPays = pays,
                        TakerGets = gets,
                        Quality = XrplQuality.FromBookDirectory(offer.BookDirectory),
                        Expiration = offer.Expiration is { } expiration ? (uint)LendingMath.RippleSeconds(expiration) : null,
                    });
                    accounts.Add(offer.Account);
                    if (pays.Kind == AmountKind.Iou)
                        AddLine(lines, offer.Account, pays.Asset.Issuer, pays.Asset.Currency);
                    if (gets.Kind == AmountKind.Iou)
                        AddLine(lines, offer.Account, gets.Asset.Issuer, gets.Asset.Currency);
                }

                if (await PoolAsync(client, @in, @out, at, cancellationToken).ConfigureAwait(false) is { } pool)
                    pools.Add(pool);
            }

            List<DexAccount> accountStates = new List<DexAccount>();
            foreach (string address in accounts)
            {
                if (address.Length == 0)
                    continue;
                if (await AccountAsync(client, address, at, cancellationToken).ConfigureAwait(false) is { } state)
                    accountStates.Add(state);
            }

            List<DexTrustLine> lineStates = new List<DexTrustLine>();
            foreach ((string a, string b, string currency) in lines)
            {
                if (await LineAsync(client, a, b, currency, at, cancellationToken).ConfigureAwait(false) is { } line)
                    lineStates.Add(line);
            }

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
            };
        }

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

        private static async Task<(ulong Base, ulong Increment)> ReservesAsync(IXrplClient client, LedgerIndex at, CancellationToken cancellationToken)
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

        private static async Task<IReadOnlyList<BookOffer>> BookAsync(
            IXrplClient client,
            string taker,
            IssuedCurrency @in,
            IssuedCurrency @out,
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
                LedgerIndex = at,
            };
            BookOffersResponse response = await client.BookOffers(request, cancellationToken).Typed().ConfigureAwait(false);
            return (IReadOnlyList<BookOffer>)response?.Offers ?? Array.Empty<BookOffer>();
        }

        private static TakerAmount TakerAmountOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp
                ? new TakerAmount { Currency = "XRP" }
                : new TakerAmount { Currency = asset.Currency, Issuer = asset.Issuer };

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

        private static async Task<DexAccount> AccountAsync(IXrplClient client, string address, LedgerIndex at, CancellationToken cancellationToken)
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
            List<string> preauthorized = new List<string>();
            if (flags.HasFlag(AccountRootFlags.lsfDepositAuth))
                preauthorized = await PreauthorizedAsync(client, address, at, cancellationToken).ConfigureAwait(false);

            return new DexAccount
            {
                Address = address,
                Balance = ulong.Parse(root.Balance.Value, CultureInfo.InvariantCulture),
                OwnerCount = root.OwnerCount ?? 0,
                TransferRate = root.TransferRate ?? 0,
                TickSize = root.TickSize ?? 0,
                GlobalFreeze = flags.HasFlag(AccountRootFlags.lsfGlobalFreeze),
                RequireAuth = flags.HasFlag(AccountRootFlags.lsfRequireAuth),
                RequireDestinationTag = flags.HasFlag(AccountRootFlags.lsfRequireDestTag),
                DepositAuth = flags.HasFlag(AccountRootFlags.lsfDepositAuth),
                DepositPreauthorized = preauthorized,
            };
        }

        /// <summary>The accounts <paramref name="address"/> preauthorized to pay it, read from its <c>DepositPreauth</c> objects.</summary>
        private static async Task<List<string>> PreauthorizedAsync(IXrplClient client, string address, LedgerIndex at, CancellationToken cancellationToken)
        {
            List<string> result = new List<string>();
            AccountObjects objects = await client
                .AccountObjects(new AccountObjectsRequest(address) { Type = LedgerEntryType.DepositPreauth, LedgerIndex = at }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            foreach (BaseLedgerEntry entry in objects?.AccountObjectList ?? new List<BaseLedgerEntry>())
            {
                if (entry is LODepositPreauth preauth && preauth.Authorize != null)
                    result.Add(preauth.Authorize);
            }

            return result;
        }

        /// <summary>The trust line between <paramref name="account"/> and <paramref name="peer"/>, from <paramref name="account"/>'s side.</summary>
        private static async Task<DexTrustLine> LineAsync(
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
            };
        }
    }
}
