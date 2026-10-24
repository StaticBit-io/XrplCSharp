using System;
using System.Collections.Generic;

using Xrpl.Utils.Hashes;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>The offer an <c>OfferCreate</c> leaves on the books, before its ledger index is known.</summary>
    /// <param name="TakerPays">What the remainder asks for.</param>
    /// <param name="TakerGets">What the remainder gives.</param>
    /// <param name="Quality">
    /// The directory it rests in: the offer's rate after tick-size rounding and before crossing,
    /// <c>uRate</c> in <c>OfferCreate::applyGuts</c>, not the remainder's.
    /// </param>
    /// <param name="Expiration">The offer's <c>Expiration</c>; null for none.</param>
    /// <param name="DomainId">The permissioned domain; null for the open book.</param>
    /// <param name="Hybrid">Whether the offer is in the open book as well (<c>tfHybrid</c>).</param>
    internal sealed record PlacedOffer(XrplAmount TakerPays, XrplAmount TakerGets, XrplQuality Quality, uint? Expiration, string DomainId, bool Hybrid);

    /// <summary>
    /// The snapshot a transaction leaves: its final view written back as ledger entries, with the
    /// index of that snapshot derived from the evaluated one's instead of built afresh.
    /// </summary>
    internal static class SnapshotRoll
    {
        private static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

        /// <summary>The snapshot as <paramref name="final"/> leaves it, its index registered.</summary>
        /// <param name="world">The world the transaction was evaluated in.</param>
        /// <param name="final">The view the result was collected from.</param>
        /// <param name="placed">The offer an <c>OfferCreate</c> placed; null for none.</param>
        /// <exception cref="InvalidOperationException">An offer was placed, and neither the account nor the transaction gives the sequence its ledger index is made from.</exception>
        internal static DexSnapshot After(DexWorld world, DexView final, PlacedOffer placed)
        {
            DexSnapshot before = world.Snapshot;
            DexIndexDelta delta = new DexIndexDelta();

            CollectAccounts(world, final, placed, delta);
            CollectLines(world, final, delta);
            CollectOffers(world, final, delta);
            if (placed != null)
                delta.Placed = Place(world, placed, delta);
            CollectPools(world, final, delta);
            CollectMpts(world, final, delta);

            DexSnapshot after = new DexSnapshot
            {
                ParentCloseTime = before.ParentCloseTime,
                LedgerSequence = before.LedgerSequence,
                ReserveBase = before.ReserveBase,
                ReserveIncrement = before.ReserveIncrement,
                Accounts = PatchAccounts(world, before.Accounts, delta),
                TrustLines = PatchLines(world, before.TrustLines, delta),
                Offers = PatchOffers(before.Offers, delta),
                Pools = PatchPools(before.Pools, delta),
                MptIssuances = PatchIssuances(before.MptIssuances, delta),
                MptHoldings = PatchHoldings(world, before.MptHoldings, delta),
                Domains = before.Domains,
                Credentials = before.Credentials,
                PartialBooks = before.PartialBooks,
                ApproximatedOwners = before.ApproximatedOwners,
            };

            DexIndex.Register(after, new DexIndex(world.Index, after, delta));
            return after;
        }

        /// <summary>
        /// <paramref name="before"/> without the offer <paramref name="offerIndex"/>, its index
        /// registered; <paramref name="before"/> itself when it does not hold the offer.
        /// </summary>
        /// <exception cref="ArgumentException">The snapshot lists a trust line or an MPT holding twice, or a hybrid offer names no domain.</exception>
        internal static DexSnapshot WithoutOffer(DexSnapshot before, string offerIndex)
        {
            DexIndex index = DexIndex.Of(before);
            if (!index.Offers.TryGetValue(offerIndex, out DexOffer offer))
                return before;

            // Keyed by the snapshot's spelling of the index, which PatchOffers matches ordinally.
            DexIndexDelta delta = new DexIndexDelta();
            delta.Offers[offer.Index] = null;

            DexSnapshot after = new DexSnapshot
            {
                ParentCloseTime = before.ParentCloseTime,
                LedgerSequence = before.LedgerSequence,
                ReserveBase = before.ReserveBase,
                ReserveIncrement = before.ReserveIncrement,
                Accounts = before.Accounts,
                TrustLines = before.TrustLines,
                Offers = PatchOffers(before.Offers, delta),
                Pools = before.Pools,
                MptIssuances = before.MptIssuances,
                MptHoldings = before.MptHoldings,
                Domains = before.Domains,
                Credentials = before.Credentials,
                PartialBooks = before.PartialBooks,
                ApproximatedOwners = before.ApproximatedOwners,
            };

            DexIndex.Register(after, new DexIndex(index, after, delta));
            return after;
        }

        private static void CollectAccounts(DexWorld world, DexView final, PlacedOffer placed, DexIndexDelta delta)
        {
            // An account the payment created is written back only when the final view funded it:
            // a tec result that fails after the creation is rolled back, as rippled discards it.
            HashSet<string> touched = new HashSet<string>(final.TouchedAccounts(), StringComparer.Ordinal);
            if (world.FeeAccount != null)
                touched.Add(world.FeeAccount);

            foreach (string address in touched)
            {
                if (world.IsPseudoAccount(address) || !world.Accounts.TryGetValue(address, out DexAccount old))
                    continue;

                long ownerCount = final.OwnerCount(address);
                uint sequence = old.Sequence;
                if (!world.Index.Accounts.ContainsKey(address))
                {
                    // Payment::doApply gives a new account the sequence of the ledger it lands in: at the
                    // earliest the one after the ledger the snapshot was read at.
                    sequence = world.Snapshot.LedgerSequence == 0 ? 0 : world.Snapshot.LedgerSequence + 1;
                }
                else if (string.Equals(address, world.FeeAccount, StringComparison.Ordinal))
                {
                    // A ticket is consumed and its object deleted instead of the sequence moving on.
                    if (world.TicketSequence != null)
                    {
                        ownerCount--;
                    }
                    else
                    {
                        uint current = CurrentSequence(world, old);
                        sequence = current == 0 ? 0 : current + 1;
                    }

                    if (placed != null)
                        ownerCount++;
                }

                delta.Accounts[address] = WithState(old, (ulong)final.Read(HoldingKey.Of(address, Xrp)).Current.StMantissa, (uint)Math.Max(0, ownerCount), sequence);
            }
        }

        /// <summary>
        /// The sequence the transaction consumes: its own, which the node requires to be the
        /// account's, or the account's in the snapshot when the transaction gives none; 0 when
        /// neither does.
        /// </summary>
        private static uint CurrentSequence(DexWorld world, DexAccount account) =>
            world.Sequence is { } sequence && sequence != 0 ? sequence : account.Sequence;

        private static void CollectLines(DexWorld world, DexView final, DexIndexDelta delta)
        {
            foreach (LineKey key in final.TouchedLines())
            {
                // A pool's lines are its balances, written back with the pool.
                if (world.IsPseudoAccount(key.First) || world.IsPseudoAccount(key.Second))
                    continue;

                LineState state = final.LineNow(key);
                if (!state.Exists)
                {
                    if (final.LineExistedBefore(key))
                        delta.Lines[key] = null;
                    continue;
                }

                delta.Lines[key] = new DexTrustLine
                {
                    Account = key.First,
                    Balance = state.Balance,
                    Limit = state.First.Limit,
                    PeerLimit = state.Second.Limit,
                    QualityIn = state.First.QualityIn,
                    QualityOut = state.First.QualityOut,
                    PeerQualityIn = state.Second.QualityIn,
                    PeerQualityOut = state.Second.QualityOut,
                    NoRipple = state.First.NoRipple,
                    PeerNoRipple = state.Second.NoRipple,
                    FrozenByAccount = state.First.Freeze,
                    Frozen = state.Second.Freeze,
                    DeepFrozen = state.DeepFrozen,
                    Authorized = state.First.Auth,
                    PeerAuthorized = state.Second.Auth,
                    Reserve = state.First.Reserve,
                    PeerReserve = state.Second.Reserve,
                };
            }
        }

        private static void CollectOffers(DexWorld world, DexView final, DexIndexDelta delta)
        {
            foreach (string index in final.TouchedOffers())
            {
                if (!world.Offers.TryGetValue(index, out DexOffer old))
                    continue;

                OfferState state = final.Offer(index);
                if (state.Deleted)
                {
                    delta.Offers[index] = null;
                    continue;
                }

                XrplAmount takerPays = state.TakerPays.WithAsset(old.TakerPays.Asset);
                XrplAmount takerGets = state.TakerGets.WithAsset(old.TakerGets.Asset);
                if (takerPays != old.TakerPays || takerGets != old.TakerGets)
                {
                    delta.Offers[index] = new DexOffer
                    {
                        Index = old.Index,
                        Account = old.Account,
                        TakerPays = takerPays,
                        TakerGets = takerGets,
                        Quality = old.Quality,
                        Expiration = old.Expiration,
                        DomainId = old.DomainId,
                        Hybrid = old.Hybrid,
                    };
                }
            }
        }

        private static DexOffer Place(DexWorld world, PlacedOffer placed, DexIndexDelta delta)
        {
            string account = world.FeeAccount;
            uint sequence = world.TicketSequence ?? CurrentSequence(world, world.Accounts[account]);
            if (sequence == 0)
            {
                throw new InvalidOperationException(
                    $"The offer {account} placed has no ledger index: the snapshot gives the account no Sequence and the transaction neither a Sequence nor a TicketSequence.");
            }

            string index = Hashes.HashOfferId(account, sequence);
            if (world.Offers.ContainsKey(index) && !(delta.Offers.TryGetValue(index, out DexOffer removed) && removed == null))
                throw new InvalidOperationException($"The snapshot already holds the offer {index} that {account} places with sequence {sequence}.");

            return new DexOffer
            {
                Index = index,
                Account = account,
                TakerPays = placed.TakerPays,
                TakerGets = placed.TakerGets,
                Quality = placed.Quality,
                Expiration = placed.Expiration,
                DomainId = placed.DomainId,
                Hybrid = placed.Hybrid,
            };
        }

        private static void CollectPools(DexWorld world, DexView final, DexIndexDelta delta)
        {
            foreach (DexAmmPool pool in world.Pools)
            {
                XrplAmount balance = final.Read(HoldingKey.Of(pool.Account, pool.Balance.Asset)).Current.WithAsset(pool.Balance.Asset);
                XrplAmount balance2 = final.Read(HoldingKey.Of(pool.Account, pool.Balance2.Asset)).Current.WithAsset(pool.Balance2.Asset);
                if (balance == pool.Balance && balance2 == pool.Balance2)
                    continue;

                // A pool's AccountRoot, when the snapshot lists it, holds the pool's XRP.
                XrplAmount? xrp = balance.Kind == AmountKind.Xrp ? balance : balance2.Kind == AmountKind.Xrp ? balance2 : null;
                if (xrp is { } drops && world.Index.Accounts.TryGetValue(pool.Account, out DexAccount poolAccount))
                    delta.Accounts[pool.Account] = WithState(poolAccount, drops.StMantissa, poolAccount.OwnerCount, poolAccount.Sequence);

                // Its lines and MPTokens, when the snapshot lists them, hold the other balances.
                WritePoolHolding(world, pool.Account, balance, delta);
                WritePoolHolding(world, pool.Account, balance2, delta);

                delta.Pools[pool.Account] = new DexAmmPool
                {
                    Account = pool.Account,
                    Balance = balance,
                    Balance2 = balance2,
                    AssetFrozen = pool.AssetFrozen,
                    Asset2Frozen = pool.Asset2Frozen,
                    TradingFee = pool.TradingFee,
                    DiscountedFee = pool.DiscountedFee,
                    SlotAccount = pool.SlotAccount,
                    SlotAuthAccounts = pool.SlotAuthAccounts,
                    SlotExpiration = pool.SlotExpiration,
                };
            }
        }

        /// <summary>The snapshot's own entry for what a pool holds of one asset, at the pool's new balance; nothing when it lists none.</summary>
        private static void WritePoolHolding(DexWorld world, string pool, XrplAmount held, DexIndexDelta delta)
        {
            if (held.Kind == AmountKind.Iou)
            {
                LineKey key = LineKey.Of(pool, held.Asset.Issuer, held.Asset.Currency);
                foreach (DexTrustLine line in world.Snapshot.TrustLines)
                {
                    if (line?.Account == null || LineKey.Of(line.Account, line.Balance.Asset.Issuer, line.Balance.Asset.Currency) != key)
                        continue;

                    bool fromPool = string.Equals(line.Account, pool, StringComparison.Ordinal);
                    delta.Lines[key] = new DexTrustLine
                    {
                        Account = line.Account,
                        Balance = fromPool ? held.WithAsset(line.Balance.Asset) : (-held).WithAsset(line.Balance.Asset),
                        Limit = line.Limit,
                        PeerLimit = line.PeerLimit,
                        QualityIn = line.QualityIn,
                        QualityOut = line.QualityOut,
                        PeerQualityIn = line.PeerQualityIn,
                        PeerQualityOut = line.PeerQualityOut,
                        NoRipple = line.NoRipple,
                        PeerNoRipple = line.PeerNoRipple,
                        Frozen = line.Frozen,
                        FrozenByAccount = line.FrozenByAccount,
                        DeepFrozen = line.DeepFrozen,
                        Authorized = line.Authorized,
                        PeerAuthorized = line.PeerAuthorized,
                        Reserve = line.Reserve,
                        PeerReserve = line.PeerReserve,
                    };
                    return;
                }

                return;
            }

            if (held.Kind != AmountKind.Mpt)
                return;

            string id = DexAssets.MptKey(held.Asset.MptIssuanceId);
            foreach (DexMptHolding holding in world.Snapshot.MptHoldings)
            {
                if (holding?.Account == null || holding.MptIssuanceId == null || !string.Equals(holding.Account, pool, StringComparison.Ordinal) ||
                    !string.Equals(DexAssets.MptKey(holding.MptIssuanceId), id, StringComparison.Ordinal))
                {
                    continue;
                }

                delta.MptHoldings[(pool, id)] = new DexMptHolding
                {
                    Account = pool,
                    MptIssuanceId = holding.MptIssuanceId,
                    Amount = held.StMantissa,
                    Locked = holding.Locked,
                    Authorized = holding.Authorized,
                };
                return;
            }
        }

        private static void CollectMpts(DexWorld world, DexView final, DexIndexDelta delta)
        {
            foreach ((string holder, string id) in final.TouchedMpTokens())
            {
                // A pool's MPToken is its balance; an issuer holds no MPToken of its own MPT.
                if (world.IsPseudoAccount(holder) || string.Equals(holder, DexAssets.MptIssuer(id), StringComparison.Ordinal))
                    continue;

                MptHoldingState state = final.MpTokenNow(holder, id);
                world.MptHoldings.TryGetValue((holder, id), out DexMptHolding old);
                if (!state.Exists)
                {
                    if (old != null)
                        delta.MptHoldings[(holder, id)] = null;
                    continue;
                }

                delta.MptHoldings[(holder, id)] = new DexMptHolding
                {
                    Account = holder,
                    MptIssuanceId = old?.MptIssuanceId ?? id,
                    Amount = checked((ulong)state.Amount),
                    Locked = state.Locked,
                    Authorized = state.Authorized,
                };
            }

            foreach (string id in final.TouchedIssuances())
            {
                if (!world.MptIssuances.TryGetValue(id, out DexMptIssuance old))
                    continue;

                ulong outstanding = final.OutstandingNow(id);
                if (outstanding == old.OutstandingAmount)
                    continue;

                delta.MptIssuances[id] = new DexMptIssuance
                {
                    MptIssuanceId = old.MptIssuanceId,
                    Issuer = old.Issuer,
                    OutstandingAmount = outstanding,
                    MaximumAmount = old.MaximumAmount,
                    TransferFee = old.TransferFee,
                    CanTrade = old.CanTrade,
                    CanTransfer = old.CanTransfer,
                    Locked = old.Locked,
                    RequireAuth = old.RequireAuth,
                    DomainId = old.DomainId,
                };
            }
        }

        private static DexAccount WithState(DexAccount account, ulong balance, uint ownerCount, uint sequence) =>
            new DexAccount
            {
                Address = account.Address,
                Balance = balance,
                OwnerCount = ownerCount,
                Sequence = sequence,
                DefaultRipple = account.DefaultRipple,
                DisallowIncomingTrustline = account.DisallowIncomingTrustline,
                TransferRate = account.TransferRate,
                TickSize = account.TickSize,
                GlobalFreeze = account.GlobalFreeze,
                RequireAuth = account.RequireAuth,
                RequireDestinationTag = account.RequireDestinationTag,
                DepositAuth = account.DepositAuth,
                DepositPreauthorized = account.DepositPreauthorized,
                DepositPreauthorizedCredentials = account.DepositPreauthorizedCredentials,
            };

        private static IReadOnlyList<DexAccount> PatchAccounts(DexWorld world, IReadOnlyList<DexAccount> accounts, DexIndexDelta delta)
        {
            if (delta.Accounts.Count == 0)
                return accounts;

            List<DexAccount> patched = new List<DexAccount>(accounts.Count + 1);
            foreach (DexAccount account in accounts)
                patched.Add(account?.Address != null && delta.Accounts.TryGetValue(account.Address, out DexAccount changed) ? changed : account);

            foreach (DexAccount account in delta.Accounts.Values)
            {
                if (!world.Index.Accounts.ContainsKey(account.Address))
                    patched.Add(account);
            }

            return patched;
        }

        private static IReadOnlyList<DexTrustLine> PatchLines(DexWorld world, IReadOnlyList<DexTrustLine> lines, DexIndexDelta delta)
        {
            if (delta.Lines.Count == 0)
                return lines;

            List<DexTrustLine> patched = new List<DexTrustLine>(lines.Count + delta.Lines.Count);
            foreach (DexTrustLine line in lines)
            {
                if (line?.Account != null &&
                    delta.Lines.TryGetValue(LineKey.Of(line.Account, line.Balance.Asset.Issuer, line.Balance.Asset.Currency), out DexTrustLine changed))
                {
                    if (changed != null)
                        patched.Add(changed);
                    continue;
                }

                patched.Add(line);
            }

            foreach (KeyValuePair<LineKey, DexTrustLine> change in delta.Lines)
            {
                if (change.Value != null && !world.Lines.ContainsKey(change.Key))
                    patched.Add(change.Value);
            }

            return patched;
        }

        private static IReadOnlyList<DexOffer> PatchOffers(IReadOnlyList<DexOffer> offers, DexIndexDelta delta)
        {
            if (delta.Offers.Count == 0 && delta.Placed == null)
                return offers;

            List<DexOffer> patched = new List<DexOffer>(offers.Count + 1);
            foreach (DexOffer offer in offers)
            {
                if (offer != null && delta.Offers.TryGetValue(offer.Index, out DexOffer changed))
                {
                    if (changed != null)
                        patched.Add(changed);
                    continue;
                }

                patched.Add(offer);
            }

            // Last, so the index sorts it after the offers already at its quality.
            if (delta.Placed != null)
                patched.Add(delta.Placed);

            return patched;
        }

        private static IReadOnlyList<DexAmmPool> PatchPools(IReadOnlyList<DexAmmPool> pools, DexIndexDelta delta)
        {
            if (delta.Pools.Count == 0)
                return pools;

            List<DexAmmPool> patched = new List<DexAmmPool>(pools.Count);
            foreach (DexAmmPool pool in pools)
                patched.Add(pool?.Account != null && delta.Pools.TryGetValue(pool.Account, out DexAmmPool changed) ? changed : pool);

            return patched;
        }

        private static IReadOnlyList<DexMptIssuance> PatchIssuances(IReadOnlyList<DexMptIssuance> issuances, DexIndexDelta delta)
        {
            if (delta.MptIssuances.Count == 0)
                return issuances;

            List<DexMptIssuance> patched = new List<DexMptIssuance>(issuances.Count);
            foreach (DexMptIssuance issuance in issuances)
            {
                patched.Add(issuance?.MptIssuanceId != null && delta.MptIssuances.TryGetValue(DexAssets.MptKey(issuance.MptIssuanceId), out DexMptIssuance changed)
                    ? changed
                    : issuance);
            }

            return patched;
        }

        private static IReadOnlyList<DexMptHolding> PatchHoldings(DexWorld world, IReadOnlyList<DexMptHolding> holdings, DexIndexDelta delta)
        {
            if (delta.MptHoldings.Count == 0)
                return holdings;

            List<DexMptHolding> patched = new List<DexMptHolding>(holdings.Count + delta.MptHoldings.Count);
            foreach (DexMptHolding holding in holdings)
            {
                if (holding?.Account != null && holding.MptIssuanceId != null &&
                    delta.MptHoldings.TryGetValue((holding.Account, DexAssets.MptKey(holding.MptIssuanceId)), out DexMptHolding changed))
                {
                    if (changed != null)
                        patched.Add(changed);
                    continue;
                }

                patched.Add(holding);
            }

            foreach (KeyValuePair<(string Account, string Id), DexMptHolding> change in delta.MptHoldings)
            {
                if (change.Value != null && !world.MptHoldings.ContainsKey(change.Key))
                    patched.Add(change.Value);
            }

            return patched;
        }
    }
}
