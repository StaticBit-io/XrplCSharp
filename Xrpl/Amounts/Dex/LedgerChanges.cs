using System;
using System.Collections.Generic;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>What a transaction changed against its snapshot: offers, pools, balances and trust lines.</summary>
    internal sealed class LedgerChanges
    {
        internal static readonly LedgerChanges None = new LedgerChanges(
            Array.Empty<OfferChange>(),
            Array.Empty<AmmPoolChange>(),
            Array.Empty<BalanceChange>(),
            Array.Empty<TrustLineChange>(),
            false);

        private LedgerChanges(
            IReadOnlyList<OfferChange> offers,
            IReadOnlyList<AmmPoolChange> pools,
            IReadOnlyList<BalanceChange> balances,
            IReadOnlyList<TrustLineChange> trustLines,
            bool reachedPartialBook)
        {
            Offers = offers;
            Pools = pools;
            Balances = balances;
            TrustLines = trustLines;
            ReachedPartialBook = reachedPartialBook;
        }

        internal IReadOnlyList<OfferChange> Offers { get; }

        internal IReadOnlyList<AmmPoolChange> Pools { get; }

        internal IReadOnlyList<BalanceChange> Balances { get; }

        internal IReadOnlyList<TrustLineChange> TrustLines { get; }

        /// <summary>Whether the engine ran off the end of a book the snapshot holds only in part.</summary>
        internal bool ReachedPartialBook { get; }

        internal static LedgerChanges Collect(DexWorld world, DexView final)
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

            HashSet<HoldingKey> keys = new HashSet<HoldingKey>(final.TouchedHoldings());
            if (world.Fee > 0)
                keys.Add(HoldingKey.Of(world.FeeAccount, new IssuedCurrency { Currency = "XRP" }));

            List<BalanceChange> balances = new List<BalanceChange>();
            foreach (HoldingKey key in keys)
            {
                if (world.PoolAccounts.Contains(key.Account) || world.PoolAccounts.Contains(key.Issuer))
                    continue;

                XrplAmount change = XrplAmountMath.ExactDifference(final.Read(key).Current, final.SnapshotBalance(key));
                if (!change.IsZero)
                    balances.Add(new BalanceChange(key.Account, change));
            }

            balances.Sort((a, b) =>
            {
                int byAccount = string.CompareOrdinal(a.Account, b.Account);
                if (byAccount != 0)
                    return byAccount;

                int byCurrency = string.CompareOrdinal(a.Change.Asset?.Currency, b.Change.Asset?.Currency);
                return byCurrency != 0 ? byCurrency : string.CompareOrdinal(a.Change.Asset?.Issuer, b.Change.Asset?.Issuer);
            });

            List<TrustLineChange> lines = new List<TrustLineChange>();
            foreach (LineKey line in final.TouchedLines())
            {
                bool before = final.LineExistedBefore(line);
                bool after = final.LineExistsNow(line);
                if (before != after)
                    lines.Add(new TrustLineChange(line.First, line.Second, line.Currency, created: after));
            }

            lines.Sort((a, b) =>
            {
                int byAccount = string.CompareOrdinal(a.Account, b.Account);
                if (byAccount != 0)
                    return byAccount;

                int byPeer = string.CompareOrdinal(a.Peer, b.Peer);
                return byPeer != 0 ? byPeer : string.CompareOrdinal(a.Currency, b.Currency);
            });

            return new LedgerChanges(offers, pools, balances, lines, world.ReachedPartialBook);
        }
    }
}
