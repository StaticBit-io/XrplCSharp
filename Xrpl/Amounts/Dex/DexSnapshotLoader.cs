using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
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
        public static async Task<DexSnapshot> FromNodeAsync(
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

            LOLedger header = await client
                .Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }, cancellationToken)
                .Typed()
                .ConfigureAwait(false);
            LedgerEntity ledger = header.LedgerEntity as LedgerEntity
                                  ?? throw new InvalidOperationException("The node returned no ledger header.");
            uint ledgerIndex = uint.Parse(ledger.LedgerIndex, System.Globalization.CultureInfo.InvariantCulture);
            LedgerIndex at = new LedgerIndex(ledgerIndex);

            (ulong reserveBase, ulong reserveIncrement) = await ReservesAsync(client, at, cancellationToken).ConfigureAwait(false);

            // Crossing, the taker pays in its TakerGets asset and receives its TakerPays asset.
            IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
            List<(IssuedCurrency In, IssuedCurrency Out)> books = new List<(IssuedCurrency, IssuedCurrency)> { (takerGets, takerPays) };
            if (XrplAmount.KindOf(takerGets) != AmountKind.Xrp && XrplAmount.KindOf(takerPays) != AmountKind.Xrp)
            {
                books.Add((takerGets, xrp));
                books.Add((xrp, takerPays));
            }

            List<DexOffer> offers = new List<DexOffer>();
            List<DexAmmPool> pools = new List<DexAmmPool>();
            HashSet<string> accounts = new HashSet<string>(StringComparer.Ordinal) { account };
            HashSet<(string Account, string Currency, string Issuer)> lines = new HashSet<(string, string, string)>();
            AddLine(lines, account, takerPays);
            AddLine(lines, account, takerGets);
            AddIssuer(accounts, takerPays);
            AddIssuer(accounts, takerGets);

            foreach ((IssuedCurrency @in, IssuedCurrency @out) in books)
            {
                foreach (BookOffer offer in await BookAsync(client, account, @in, @out, at, cancellationToken).ConfigureAwait(false))
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
                    AddLine(lines, offer.Account, pays.Asset);
                    AddLine(lines, offer.Account, gets.Asset);
                }

                if (await PoolAsync(client, @in, @out, at, cancellationToken).ConfigureAwait(false) is { } pool)
                    pools.Add(pool);
            }

            List<DexAccount> accountStates = new List<DexAccount>();
            foreach (string address in accounts)
            {
                if (await AccountAsync(client, address, at, cancellationToken).ConfigureAwait(false) is { } state)
                    accountStates.Add(state);
            }

            List<DexTrustLine> lineStates = new List<DexTrustLine>();
            foreach ((string holder, string currency, string issuer) in lines)
            {
                if (await LineAsync(client, holder, currency, issuer, at, cancellationToken).ConfigureAwait(false) is { } line)
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

        private static void AddLine(HashSet<(string, string, string)> lines, string holder, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Iou && !string.Equals(holder, asset.Issuer, StringComparison.Ordinal))
                lines.Add((holder, asset.Currency, asset.Issuer));
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
                return (ulong.Parse(fees.ReserveBaseDrops, System.Globalization.CultureInfo.InvariantCulture), ulong.Parse(fees.ReserveIncrementDrops, System.Globalization.CultureInfo.InvariantCulture));

            return (fees.ReserveBase ?? 0, fees.ReserveIncrement ?? 0);
        }

        private static async Task<IReadOnlyList<BookOffer>> BookAsync(
            IXrplClient client,
            string account,
            IssuedCurrency @in,
            IssuedCurrency @out,
            LedgerIndex at,
            CancellationToken cancellationToken)
        {
            // With the taker named, the account's own unfunded offers are returned too.
            BookOffersRequest request = new BookOffersRequest
            {
                Taker = account,
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

            return new DexAccount
            {
                Address = address,
                Balance = ulong.Parse(root.Balance.Value, System.Globalization.CultureInfo.InvariantCulture),
                OwnerCount = root.OwnerCount ?? 0,
                TransferRate = root.TransferRate ?? 0,
                TickSize = root.TickSize ?? 0,
                GlobalFreeze = root.Flags is { } flags && flags.HasFlag(AccountRootFlags.lsfGlobalFreeze),
            };
        }

        private static async Task<DexTrustLine> LineAsync(
            IXrplClient client,
            string holder,
            string currency,
            string issuer,
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
                            RippleState = new RippleStateQuery { Addresses = new[] { holder, issuer }, Currency = currency },
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

            IssuedCurrency asset = new IssuedCurrency { Currency = currency, Issuer = issuer };
            bool holderIsLow = string.Equals(line.LowLimit?.Issuer, holder, StringComparison.Ordinal);
            XrplAmount balance = XrplAmount.FromNumber(asset, line.Balance.ToXrplAmount().Value);
            RippleStateFlags flags = line.Flags ?? 0;
            RippleStateFlags issuerFreeze = holderIsLow ? RippleStateFlags.lsfHighFreeze : RippleStateFlags.lsfLowFreeze;
            return new DexTrustLine
            {
                Account = holder,
                Balance = holderIsLow ? balance : -balance,
                Frozen = flags.HasFlag(issuerFreeze),
                DeepFrozen = flags.HasFlag(RippleStateFlags.lsfLowDeepFreeze) || flags.HasFlag(RippleStateFlags.lsfHighDeepFreeze),
            };
        }
    }
}
