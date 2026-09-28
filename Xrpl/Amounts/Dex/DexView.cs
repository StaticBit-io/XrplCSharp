using System;
using System.Collections.Generic;

using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>Who holds what: an account and an asset, XRP keyed by an empty issuer.</summary>
    internal readonly record struct HoldingKey(string Account, string Currency, string Issuer)
    {
        internal static HoldingKey Of(string account, IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp
                ? new HoldingKey(account, "XRP", string.Empty)
                : new HoldingKey(account, asset.Currency, asset.Issuer);
    }

    /// <summary>The order books a snapshot covers, keyed by the asset taken in and the asset given out.</summary>
    internal readonly record struct BookKey(string InCurrency, string InIssuer, string OutCurrency, string OutIssuer)
    {
        internal static BookKey Of(IssuedCurrency @in, IssuedCurrency @out)
        {
            HoldingKey i = HoldingKey.Of(string.Empty, @in);
            HoldingKey o = HoldingKey.Of(string.Empty, @out);
            return new BookKey(i.Currency, i.Issuer, o.Currency, o.Issuer);
        }
    }

    /// <summary>The fixed part of a snapshot, indexed for the crossing.</summary>
    internal sealed class DexWorld
    {
        internal DexWorld(DexSnapshot snapshot, string feeAccount = null, ulong fee = 0)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            FeeAccount = feeAccount;
            Fee = fee;
            foreach (DexAccount account in snapshot.Accounts ?? Array.Empty<DexAccount>())
            {
                if (account?.Address != null)
                    Accounts[account.Address] = account;
            }

            foreach (DexTrustLine line in snapshot.TrustLines ?? Array.Empty<DexTrustLine>())
            {
                if (line?.Account == null)
                    continue;
                if (XrplAmount.KindOf(line.Balance.Asset) != AmountKind.Iou)
                    throw new NotSupportedException("A trust line holds an issued currency.");

                Lines[HoldingKey.Of(line.Account, line.Balance.Asset)] = line;
            }

            foreach (DexOffer offer in snapshot.Offers ?? Array.Empty<DexOffer>())
            {
                if (offer == null)
                    continue;
                if (offer.TakerPays.Kind == AmountKind.Mpt || offer.TakerGets.Kind == AmountKind.Mpt)
                    throw new NotSupportedException("MPT order books are not supported.");

                // Crossing, the taker pays what the offer asks for and gets what it gives.
                BookKey book = BookKey.Of(offer.TakerPays.Asset, offer.TakerGets.Asset);
                if (!Books.TryGetValue(book, out List<DexOffer> list))
                    Books[book] = list = new List<DexOffer>();

                list.Add(offer);
                Offers[offer.Index] = offer;
            }

            // Directories in quality order, better first; within one, the order of the snapshot.
            foreach (List<DexOffer> list in Books.Values)
                StableSortByQuality(list);

            foreach (DexAmmPool pool in snapshot.Pools ?? Array.Empty<DexAmmPool>())
            {
                if (pool?.Account == null)
                    continue;
                if (pool.Balance.Kind == AmountKind.Mpt || pool.Balance2.Kind == AmountKind.Mpt)
                    throw new NotSupportedException("MPT pools are not supported.");

                Pools.Add(pool);
            }
        }

        internal DexSnapshot Snapshot { get; }

        /// <summary>The account that paid the transaction's fee, charged before anything is crossed.</summary>
        internal string FeeAccount { get; }

        /// <summary>The fee, in drops.</summary>
        internal ulong Fee { get; }

        internal Dictionary<string, DexAccount> Accounts { get; } = new Dictionary<string, DexAccount>(StringComparer.Ordinal);

        internal Dictionary<HoldingKey, DexTrustLine> Lines { get; } = new Dictionary<HoldingKey, DexTrustLine>();

        internal Dictionary<BookKey, List<DexOffer>> Books { get; } = new Dictionary<BookKey, List<DexOffer>>();

        internal Dictionary<string, DexOffer> Offers { get; } = new Dictionary<string, DexOffer>(StringComparer.Ordinal);

        internal List<DexAmmPool> Pools { get; } = new List<DexAmmPool>();

        internal IReadOnlyList<DexOffer> Book(IssuedCurrency @in, IssuedCurrency @out) =>
            Books.TryGetValue(BookKey.Of(@in, @out), out List<DexOffer> list) ? list : Array.Empty<DexOffer>();

        /// <summary>The pool that trades these two assets; null when there is none.</summary>
        internal DexAmmPool Pool(IssuedCurrency a, IssuedCurrency b)
        {
            foreach (DexAmmPool pool in Pools)
            {
                if ((XrplAmount.SameAsset(pool.Balance.Asset, a) && XrplAmount.SameAsset(pool.Balance2.Asset, b)) ||
                    (XrplAmount.SameAsset(pool.Balance.Asset, b) && XrplAmount.SameAsset(pool.Balance2.Asset, a)))
                {
                    return pool;
                }
            }

            return null;
        }

        /// <summary>The issuer's transfer rate, <see cref="OfferCrossing.QualityOne"/> for none.</summary>
        internal uint TransferRate(string issuer)
        {
            if (issuer == null || !Accounts.TryGetValue(issuer, out DexAccount account) || account.TransferRate == 0)
                return OfferCrossing.QualityOne;

            return account.TransferRate;
        }

        private static void StableSortByQuality(List<DexOffer> offers)
        {
            // List.Sort is not stable: an insertion sort keeps the snapshot's order within a quality.
            for (int i = 1; i < offers.Count; i++)
            {
                DexOffer current = offers[i];
                int j = i - 1;
                while (j >= 0 && offers[j].Quality.Value > current.Quality.Value)
                {
                    offers[j + 1] = offers[j];
                    j--;
                }

                offers[j + 1] = current;
            }
        }
    }

    /// <summary>What an account holds of one asset while a crossing runs.</summary>
    internal sealed class Holding
    {
        /// <summary>The balance now.</summary>
        internal XrplAmount Current { get; set; }

        /// <summary>What the account has sent since the transaction began: <c>DeferredCredits</c>' debits.</summary>
        internal XrplAmount Debits { get; set; }

        /// <summary>Whether the trust line exists; XRP always does.</summary>
        internal bool Exists { get; set; }

        internal Holding Copy() => new Holding { Current = Current, Debits = Debits, Exists = Exists };
    }

    /// <summary>An account's owner count while a crossing runs, and the largest it has been (<c>ownerCountHook</c>).</summary>
    internal sealed class OwnerCounts
    {
        internal long Actual { get; set; }

        internal long Max { get; set; }

        internal OwnerCounts Copy() => new OwnerCounts { Actual = Actual, Max = Max };
    }

    /// <summary>What is left of an offer while a crossing runs.</summary>
    internal sealed class OfferState
    {
        internal XrplAmount TakerPays { get; set; }

        internal XrplAmount TakerGets { get; set; }

        internal bool Deleted { get; set; }

        internal OfferState Copy() => new OfferState { TakerPays = TakerPays, TakerGets = TakerGets, Deleted = Deleted };
    }

    /// <summary>
    /// A layer of changes over a snapshot, as rippled's <c>PaymentSandbox</c> layers over a
    /// ledger: reads fall through to the parent, writes stay here until <see cref="ApplyTo"/>.
    /// Balances follow <c>DeferredCredits</c> - what an account receives during the transaction
    /// cannot be spent in it - and the owner count used for the reserve never drops below the
    /// largest it reached.
    /// </summary>
    internal sealed class DexView
    {
        private readonly Dictionary<HoldingKey, Holding> _holdings = new Dictionary<HoldingKey, Holding>();
        private readonly Dictionary<string, OfferState> _offers = new Dictionary<string, OfferState>(StringComparer.Ordinal);
        private readonly Dictionary<string, OwnerCounts> _counts = new Dictionary<string, OwnerCounts>(StringComparer.Ordinal);
        private readonly DexView _parent;

        internal DexView(DexWorld world, LedgerRules rules)
        {
            World = world;
            Rules = rules;
        }

        internal DexView(DexView parent)
        {
            _parent = parent;
            World = parent.World;
            Rules = parent.Rules;
        }

        internal DexWorld World { get; }

        internal LedgerRules Rules { get; }

        internal uint ParentCloseTime => World.Snapshot.ParentCloseTime;

        /// <summary>Writes this layer's changes into its parent.</summary>
        internal void ApplyTo(DexView parent)
        {
            foreach (KeyValuePair<HoldingKey, Holding> entry in _holdings)
                parent._holdings[entry.Key] = entry.Value.Copy();
            foreach (KeyValuePair<string, OfferState> entry in _offers)
                parent._offers[entry.Key] = entry.Value.Copy();
            foreach (KeyValuePair<string, OwnerCounts> entry in _counts)
                parent._counts[entry.Key] = entry.Value.Copy();
        }

        /// <summary>Every holding this view or a parent has written.</summary>
        internal IEnumerable<HoldingKey> TouchedHoldings()
        {
            HashSet<HoldingKey> keys = new HashSet<HoldingKey>();
            for (DexView view = this; view != null; view = view._parent)
                keys.UnionWith(view._holdings.Keys);
            return keys;
        }

        /// <summary>The holding as the snapshot has it, before the transaction's fee.</summary>
        internal XrplAmount SnapshotBalance(HoldingKey key)
        {
            XrplAmount start = Initial(key).Current;
            if (key.Issuer.Length == 0 && string.Equals(key.Account, World.FeeAccount, StringComparison.Ordinal) &&
                World.Accounts.TryGetValue(key.Account, out DexAccount account))
            {
                return XrplAmount.FromUnits(start.Asset, AmountKind.Xrp, checked((long)account.Balance));
            }

            return start;
        }

        // ---- offers ----

        internal OfferState Offer(string index)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._offers.TryGetValue(index, out OfferState state))
                    return state;
            }

            DexOffer offer = World.Offers[index];
            return new OfferState { TakerPays = offer.TakerPays, TakerGets = offer.TakerGets };
        }

        /// <summary><c>TOffer::consume</c>: the offer reduced by what was taken from it.</summary>
        internal void ConsumeOffer(string index, XrplAmount consumedIn, XrplAmount consumedOut)
        {
            OfferState state = OfferForWrite(index);
            if (consumedIn > state.TakerPays)
                throw new InvalidOperationException("can't consume more than is available.");
            if (consumedOut > state.TakerGets)
                throw new InvalidOperationException("can't produce more than is available.");

            state.TakerPays = StepMath.Subtract(state.TakerPays, consumedIn, Rules);
            state.TakerGets = StepMath.Subtract(state.TakerGets, consumedOut, Rules);
        }

        /// <summary><c>offerDelete</c>: the offer leaves the book and its owner owns one object less.</summary>
        internal void DeleteOffer(string index)
        {
            OfferState state = OfferForWrite(index);
            if (state.Deleted)
                return;

            state.Deleted = true;
            AdjustOwnerCount(World.Offers[index].Account, -1);
        }

        private OfferState OfferForWrite(string index)
        {
            if (!_offers.TryGetValue(index, out OfferState state))
                _offers[index] = state = Offer(index).Copy();

            return state;
        }

        // ---- balances ----

        internal Holding Read(HoldingKey key)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._holdings.TryGetValue(key, out Holding holding))
                    return holding;
            }

            return Initial(key);
        }

        private Holding Initial(HoldingKey key)
        {
            if (key.Issuer.Length == 0)
            {
                IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
                bool exists = World.Accounts.TryGetValue(key.Account, out DexAccount account);
                XrplAmount balance = XrplAmount.Zero(xrp);
                if (exists)
                {
                    ulong drops = account.Balance;
                    if (string.Equals(key.Account, World.FeeAccount, StringComparison.Ordinal))
                        drops = drops >= World.Fee ? drops - World.Fee : 0;
                    balance = XrplAmount.FromUnits(xrp, AmountKind.Xrp, checked((long)drops));
                }

                foreach (DexAmmPool pool in World.Pools)
                {
                    if (string.Equals(pool.Account, key.Account, StringComparison.Ordinal))
                        balance = PoolBalance(pool, xrp) ?? balance;
                }

                return new Holding { Current = balance, Debits = XrplAmount.Zero(xrp), Exists = exists };
            }

            IssuedCurrency asset = new IssuedCurrency { Currency = key.Currency, Issuer = key.Issuer };
            foreach (DexAmmPool pool in World.Pools)
            {
                if (string.Equals(pool.Account, key.Account, StringComparison.Ordinal) && PoolBalance(pool, asset) is { } held)
                    return new Holding { Current = held, Debits = XrplAmount.Zero(asset), Exists = true };
            }

            if (World.Lines.TryGetValue(key, out DexTrustLine line))
                return new Holding { Current = line.Balance, Debits = XrplAmount.Zero(asset), Exists = true };

            return new Holding { Current = XrplAmount.Zero(asset), Debits = XrplAmount.Zero(asset), Exists = false };
        }

        private static XrplAmount? PoolBalance(DexAmmPool pool, IssuedCurrency asset)
        {
            if (XrplAmount.SameAsset(pool.Balance.Asset, asset))
                return pool.Balance;
            if (XrplAmount.SameAsset(pool.Balance2.Asset, asset))
                return pool.Balance2;
            return null;
        }

        private Holding HoldingForWrite(HoldingKey key)
        {
            if (!_holdings.TryGetValue(key, out Holding holding))
                _holdings[key] = holding = Read(key).Copy();

            return holding;
        }

        /// <summary>
        /// <c>accountSend</c> without a transfer fee: <paramref name="amount"/> from
        /// <paramref name="from"/> to <paramref name="to"/>, where the issuer of an issued
        /// currency - and the empty account for XRP - creates and destroys it.
        /// </summary>
        internal void Send(string from, string to, XrplAmount amount)
        {
            if (amount.IsZero || string.Equals(from, to, StringComparison.Ordinal))
                return;

            string issuer = amount.Kind == AmountKind.Xrp ? string.Empty : amount.Asset.Issuer;
            if (!string.Equals(from, issuer, StringComparison.Ordinal))
            {
                Holding sender = HoldingForWrite(HoldingKey.Of(from, amount.Asset));
                sender.Current = StepMath.Subtract(sender.Current, amount, Rules);
                sender.Debits = StepMath.Add(sender.Debits, amount, Rules);
            }

            if (!string.Equals(to, issuer, StringComparison.Ordinal))
            {
                Holding receiver = HoldingForWrite(HoldingKey.Of(to, amount.Asset));
                receiver.Current = StepMath.Add(receiver.Current, amount, Rules);
                if (!receiver.Exists && amount.Kind == AmountKind.Iou)
                {
                    // The trust line is created on the way in, and the receiver owns it.
                    receiver.Exists = true;
                    AdjustOwnerCount(to, 1);
                }
            }
        }

        /// <summary>
        /// <c>balanceHook</c>: the balance less what was received during the transaction - the
        /// balance before it, less what has been sent since - and never more than the balance now.
        /// </summary>
        internal XrplAmount Spendable(HoldingKey key)
        {
            Holding holding = Read(key);
            XrplAmount original = Initial(key).Current;
            XrplAmount usable = StepMath.Subtract(original, holding.Debits, Rules);
            XrplAmount result = StepMath.Min(holding.Current, StepMath.Min(usable, original));
            if (key.Issuer.Length == 0 && result.IsNegative)
                return XrplAmount.Zero(result.Asset);

            return result;
        }

        /// <summary>What the pool's account holds, frozen assets excepted (<c>ammAccountHolds</c>).</summary>
        internal XrplAmount PoolHolds(DexAmmPool pool, IssuedCurrency asset)
        {
            bool frozen = XrplAmount.SameAsset(pool.Balance.Asset, asset) ? pool.AssetFrozen : pool.Asset2Frozen;
            if (frozen)
                return XrplAmount.Zero(asset);

            return Read(HoldingKey.Of(pool.Account, asset)).Current;
        }

        // ---- owner counts ----

        private OwnerCounts Counts(string account)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._counts.TryGetValue(account, out OwnerCounts counts))
                    return counts;
            }

            long initial = World.Accounts.TryGetValue(account, out DexAccount dex) ? dex.OwnerCount : 0;
            return new OwnerCounts { Actual = initial, Max = initial };
        }

        private void AdjustOwnerCount(string account, int delta)
        {
            if (!_counts.TryGetValue(account, out OwnerCounts counts))
                _counts[account] = counts = Counts(account).Copy();

            long next = Math.Max(0, counts.Actual + delta);
            counts.Max = Math.Max(counts.Max, Math.Max(counts.Actual, next));
            counts.Actual = next;
        }

        /// <summary>The owner count the reserve is computed from during the transaction.</summary>
        internal long ReserveOwnerCount(string account)
        {
            OwnerCounts counts = Counts(account);
            return Math.Max(counts.Actual, counts.Max);
        }

        /// <summary>The owner count as the ledger holds it, which placing the offer checks.</summary>
        internal long OwnerCount(string account) => Counts(account).Actual;

        // ---- funds ----

        /// <summary>
        /// <c>xrpLiquid</c>: the spendable XRP above the reserve, the owner count adjusted by
        /// <paramref name="ownerCountAdjustment"/>.
        /// </summary>
        internal XrplAmount XrpLiquid(string account, int ownerCountAdjustment = 0)
        {
            IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
            if (!World.Accounts.ContainsKey(account))
                return XrplAmount.Zero(xrp);

            long ownerCount = Math.Max(0, ReserveOwnerCount(account) + ownerCountAdjustment);
            decimal reserve = World.Snapshot.ReserveBase + (decimal)World.Snapshot.ReserveIncrement * ownerCount;
            XrplAmount balance = Spendable(HoldingKey.Of(account, xrp));
            decimal drops = (decimal)balance.StMantissa;
            if (drops < reserve)
                return XrplAmount.Zero(xrp);

            return XrplAmount.FromUnits(xrp, AmountKind.Xrp, (long)(drops - reserve));
        }

        /// <summary>Whether the issuer or the line freezes <paramref name="account"/>'s holding (<c>isFrozen</c>).</summary>
        internal bool IsFrozen(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) != AmountKind.Iou)
                return false;
            if (World.Accounts.TryGetValue(asset.Issuer, out DexAccount issuer) && issuer.GlobalFreeze)
                return true;
            if (string.Equals(account, asset.Issuer, StringComparison.Ordinal))
                return false;

            return World.Lines.TryGetValue(HoldingKey.Of(account, asset), out DexTrustLine line) && line.Frozen;
        }

        /// <summary><c>isDeepFrozen</c>.</summary>
        internal bool IsDeepFrozen(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) != AmountKind.Iou || string.Equals(account, asset.Issuer, StringComparison.Ordinal))
                return false;

            return World.Lines.TryGetValue(HoldingKey.Of(account, asset), out DexTrustLine line) && line.DeepFrozen;
        }

        /// <summary><c>accountHolds</c> with <c>ZeroIfFrozen</c>.</summary>
        internal XrplAmount AccountHolds(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Xrp)
                return XrpLiquid(account);

            HoldingKey key = HoldingKey.Of(account, asset);
            if (!Read(key).Exists || IsFrozen(account, asset) || IsDeepFrozen(account, asset))
                return XrplAmount.Zero(asset);

            return Spendable(key);
        }

        /// <summary><c>accountFunds</c>: <paramref name="whenIssuer"/> for the issuer of an issued currency, otherwise <see cref="AccountHolds"/>.</summary>
        internal XrplAmount AccountFunds(string account, XrplAmount whenIssuer)
        {
            if (whenIssuer.Kind == AmountKind.Iou && string.Equals(account, whenIssuer.Asset.Issuer, StringComparison.Ordinal))
                return whenIssuer;

            return AccountHolds(account, whenIssuer.Asset);
        }

        /// <summary><c>transferRate</c> as a step sees it: none for XRP or when the issuer is <paramref name="strandDestination"/>.</summary>
        internal uint Rate(IssuedCurrency asset, string strandDestination)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Xrp || string.Equals(asset.Issuer, strandDestination, StringComparison.Ordinal))
                return OfferCrossing.QualityOne;

            return World.TransferRate(asset.Issuer);
        }
    }
}
