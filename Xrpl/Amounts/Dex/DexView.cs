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

    /// <summary>A trust line's identity: its two accounts in ordinal order and the currency.</summary>
    internal readonly record struct LineKey(string First, string Second, string Currency)
    {
        internal static LineKey Of(string a, string b, string currency) =>
            string.CompareOrdinal(a, b) <= 0 ? new LineKey(a, b, currency) : new LineKey(b, a, currency);

        internal bool IsFirst(string account) => string.Equals(First, account, StringComparison.Ordinal);

        internal string PeerOf(string account) => IsFirst(account) ? Second : First;
    }

    /// <summary>One account's settings on a trust line.</summary>
    internal sealed class LineSide
    {
        internal static readonly LineSide Default = new LineSide();

        /// <summary>How much of the peer's currency this account trusts it for; null for zero.</summary>
        internal XrplAmount? Limit { get; init; }

        internal uint QualityIn { get; init; }

        internal uint QualityOut { get; init; }

        internal bool NoRipple { get; init; }

        /// <summary>Whether this account froze the line.</summary>
        internal bool Freeze { get; init; }

        /// <summary>Whether this account authorized the peer to hold its currency.</summary>
        internal bool Auth { get; init; }
    }

    /// <summary>A trust line's fixed settings, as the snapshot gives them.</summary>
    internal sealed class LineInfo
    {
        internal LineKey Key { get; init; }

        internal LineSide First { get; init; } = LineSide.Default;

        internal LineSide Second { get; init; } = LineSide.Default;

        internal bool DeepFrozen { get; init; }

        /// <summary>The balance from the first account's side, the second as the issuer.</summary>
        internal XrplAmount Balance { get; init; }

        internal LineSide Side(string account) => Key.IsFirst(account) ? First : Second;
    }

    /// <summary>The fixed part of a snapshot, indexed for the engine.</summary>
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

                string peer = line.Balance.Asset.Issuer;
                LineKey key = LineKey.Of(line.Account, peer, line.Balance.Asset.Currency);
                LineSide side = new LineSide
                {
                    Limit = line.Limit,
                    QualityIn = line.QualityIn,
                    QualityOut = line.QualityOut,
                    NoRipple = line.NoRipple,
                    Freeze = line.FrozenByAccount,
                    Auth = line.Authorized,
                };
                LineSide peerSide = new LineSide
                {
                    Limit = line.PeerLimit,
                    QualityIn = line.PeerQualityIn,
                    QualityOut = line.PeerQualityOut,
                    NoRipple = line.PeerNoRipple,
                    Freeze = line.Frozen,
                    Auth = line.PeerAuthorized,
                };
                bool accountFirst = key.IsFirst(line.Account);
                if (Lines.ContainsKey(key))
                {
                    throw new ArgumentException(
                        $"The trust line {key.First}/{key.Second} {key.Currency} is listed twice; describe it once, with the other side in its Peer properties.");
                }

                Lines[key] = new LineInfo
                {
                    Key = key,
                    First = accountFirst ? side : peerSide,
                    Second = accountFirst ? peerSide : side,
                    DeepFrozen = line.DeepFrozen,
                    Balance = FirstView(key, line.Account, line.Balance),
                };
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
                PoolAccounts.Add(pool.Account);
                foreach (XrplAmount held in new[] { pool.Balance, pool.Balance2 })
                {
                    if (held.Kind != AmountKind.Iou)
                        continue;

                    // The pool's balance is what its account holds on the line to the issuer.
                    LineKey key = LineKey.Of(pool.Account, held.Asset.Issuer, held.Asset.Currency);
                    Lines[key] = new LineInfo { Key = key, Balance = FirstView(key, pool.Account, held) };
                }
            }
        }

        internal DexSnapshot Snapshot { get; }

        /// <summary>The account that paid the transaction's fee, charged before anything moves.</summary>
        internal string FeeAccount { get; }

        /// <summary>The fee, in drops.</summary>
        internal ulong Fee { get; }

        internal Dictionary<string, DexAccount> Accounts { get; } = new Dictionary<string, DexAccount>(StringComparer.Ordinal);

        internal Dictionary<LineKey, LineInfo> Lines { get; } = new Dictionary<LineKey, LineInfo>();

        internal Dictionary<BookKey, List<DexOffer>> Books { get; } = new Dictionary<BookKey, List<DexOffer>>();

        internal Dictionary<string, DexOffer> Offers { get; } = new Dictionary<string, DexOffer>(StringComparer.Ordinal);

        internal List<DexAmmPool> Pools { get; } = new List<DexAmmPool>();

        internal HashSet<string> PoolAccounts { get; } = new HashSet<string>(StringComparer.Ordinal);

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

        /// <summary>Whether the account exists: in the snapshot, or an AMM's.</summary>
        internal bool AccountExists(string account) => account != null && (Accounts.ContainsKey(account) || PoolAccounts.Contains(account));

        /// <summary>A balance seen from <paramref name="holder"/> turned to the line's first account's side.</summary>
        internal static XrplAmount FirstView(LineKey key, string holder, XrplAmount balance)
        {
            IssuedCurrency firstAsset = new IssuedCurrency { Currency = key.Currency, Issuer = key.Second };
            return key.IsFirst(holder) ? balance.WithAsset(firstAsset) : (-balance).WithAsset(firstAsset);
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

    /// <summary>What an account holds of one asset, as a view reads it.</summary>
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

    /// <summary>A trust line's balance while the transaction runs, and what each side has sent.</summary>
    internal sealed class LineState
    {
        /// <summary>The balance from the first account's side.</summary>
        internal XrplAmount Balance { get; set; }

        internal XrplAmount FirstDebits { get; set; }

        internal XrplAmount SecondDebits { get; set; }

        internal bool Exists { get; set; }

        internal LineState Copy() =>
            new LineState { Balance = Balance, FirstDebits = FirstDebits, SecondDebits = SecondDebits, Exists = Exists };
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
        private static readonly IssuedCurrency XrpAsset = new IssuedCurrency { Currency = "XRP" };

        private readonly Dictionary<string, Holding> _xrp = new Dictionary<string, Holding>(StringComparer.Ordinal);
        private readonly Dictionary<LineKey, LineState> _lines = new Dictionary<LineKey, LineState>();
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
            foreach (KeyValuePair<string, Holding> entry in _xrp)
                parent._xrp[entry.Key] = entry.Value.Copy();
            foreach (KeyValuePair<LineKey, LineState> entry in _lines)
                parent._lines[entry.Key] = entry.Value.Copy();
            foreach (KeyValuePair<string, OfferState> entry in _offers)
                parent._offers[entry.Key] = entry.Value.Copy();
            foreach (KeyValuePair<string, OwnerCounts> entry in _counts)
                parent._counts[entry.Key] = entry.Value.Copy();
        }

        /// <summary>Both sides of every trust line, and every XRP balance, this view or a parent has written.</summary>
        internal IEnumerable<HoldingKey> TouchedHoldings()
        {
            HashSet<HoldingKey> keys = new HashSet<HoldingKey>();
            for (DexView view = this; view != null; view = view._parent)
            {
                foreach (string account in view._xrp.Keys)
                    keys.Add(new HoldingKey(account, "XRP", string.Empty));
                foreach (LineKey line in view._lines.Keys)
                {
                    keys.Add(new HoldingKey(line.First, line.Currency, line.Second));
                    keys.Add(new HoldingKey(line.Second, line.Currency, line.First));
                }
            }

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

        /// <summary>What <see cref="HoldingKey.Account"/> holds: its XRP, or its side of the line to <see cref="HoldingKey.Issuer"/>.</summary>
        internal Holding Read(HoldingKey key)
        {
            if (key.Issuer.Length == 0)
                return ReadXrp(key.Account);

            if (string.Equals(key.Account, key.Issuer, StringComparison.Ordinal))
            {
                IssuedCurrency own = new IssuedCurrency { Currency = key.Currency, Issuer = key.Issuer };
                return new Holding { Current = XrplAmount.Zero(own), Debits = XrplAmount.Zero(own), Exists = false };
            }

            LineKey line = LineKey.Of(key.Account, key.Issuer, key.Currency);
            return SideOf(line, key.Account, ReadLine(line));
        }

        private Holding Initial(HoldingKey key)
        {
            if (key.Issuer.Length == 0)
                return InitialXrp(key.Account);

            LineKey line = LineKey.Of(key.Account, key.Issuer, key.Currency);
            return SideOf(line, key.Account, InitialLine(line));
        }

        private static Holding SideOf(LineKey line, string account, LineState state)
        {
            IssuedCurrency asset = new IssuedCurrency { Currency = line.Currency, Issuer = line.PeerOf(account) };
            bool first = line.IsFirst(account);
            XrplAmount balance = first ? state.Balance : -state.Balance;
            XrplAmount debits = first ? state.FirstDebits : state.SecondDebits;
            return new Holding { Current = balance.WithAsset(asset), Debits = debits.WithAsset(asset), Exists = state.Exists };
        }

        private Holding ReadXrp(string account)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._xrp.TryGetValue(account, out Holding holding))
                    return holding;
            }

            return InitialXrp(account);
        }

        private Holding InitialXrp(string account)
        {
            bool exists = World.Accounts.TryGetValue(account, out DexAccount dex);
            XrplAmount balance = XrplAmount.Zero(XrpAsset);
            if (exists)
            {
                ulong drops = dex.Balance;
                if (string.Equals(account, World.FeeAccount, StringComparison.Ordinal))
                    drops = drops >= World.Fee ? drops - World.Fee : 0;
                balance = XrplAmount.FromUnits(XrpAsset, AmountKind.Xrp, checked((long)drops));
            }

            foreach (DexAmmPool pool in World.Pools)
            {
                if (string.Equals(pool.Account, account, StringComparison.Ordinal))
                    balance = PoolBalance(pool, XrpAsset) ?? balance;
            }

            return new Holding { Current = balance, Debits = XrplAmount.Zero(XrpAsset), Exists = exists };
        }

        private LineState ReadLine(LineKey line)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._lines.TryGetValue(line, out LineState state))
                    return state;
            }

            return InitialLine(line);
        }

        private LineState InitialLine(LineKey line)
        {
            IssuedCurrency asset = new IssuedCurrency { Currency = line.Currency, Issuer = line.Second };
            XrplAmount zero = XrplAmount.Zero(asset);
            return World.Lines.TryGetValue(line, out LineInfo info)
                ? new LineState { Balance = info.Balance, FirstDebits = zero, SecondDebits = zero, Exists = true }
                : new LineState { Balance = zero, FirstDebits = zero, SecondDebits = zero, Exists = false };
        }

        private static XrplAmount? PoolBalance(DexAmmPool pool, IssuedCurrency asset)
        {
            if (XrplAmount.SameAsset(pool.Balance.Asset, asset))
                return pool.Balance;
            if (XrplAmount.SameAsset(pool.Balance2.Asset, asset))
                return pool.Balance2;
            return null;
        }

        /// <summary>Whether the trust line between the two accounts exists now.</summary>
        internal bool LineExists(string a, string b, string currency) =>
            !string.Equals(a, b, StringComparison.Ordinal) && ReadLine(LineKey.Of(a, b, currency)).Exists;

        /// <summary>The line's fixed settings; null when the snapshot has no such line.</summary>
        internal LineInfo LineInfo(string a, string b, string currency) =>
            World.Lines.TryGetValue(LineKey.Of(a, b, currency), out LineInfo info) ? info : null;

        /// <summary>
        /// <c>accountSend</c> without a transfer fee: <c>rippleCredit</c> on the trust line between
        /// the two accounts for an issued currency, or XRP between an account and the empty account.
        /// </summary>
        internal void Send(string from, string to, XrplAmount amount)
        {
            if (amount.IsZero || string.Equals(from, to, StringComparison.Ordinal))
                return;

            if (amount.Kind == AmountKind.Xrp)
            {
                if (from.Length != 0)
                {
                    Holding sender = XrpForWrite(from);
                    sender.Current = StepMath.Subtract(sender.Current, amount, Rules);
                    sender.Debits = StepMath.Add(sender.Debits, amount, Rules);
                }

                if (to.Length != 0)
                {
                    Holding receiver = XrpForWrite(to);
                    receiver.Current = StepMath.Add(receiver.Current, amount, Rules);
                }

                return;
            }

            LineKey line = LineKey.Of(from, to, amount.Asset.Currency);
            LineState state = LineForWrite(line);
            XrplAmount moved = amount.WithAsset(state.Balance.Asset);
            if (line.IsFirst(from))
            {
                state.Balance = StepMath.Subtract(state.Balance, moved, Rules);
                state.FirstDebits = StepMath.Add(state.FirstDebits, moved, Rules);
            }
            else
            {
                state.Balance = StepMath.Add(state.Balance, moved, Rules);
                state.SecondDebits = StepMath.Add(state.SecondDebits, moved, Rules);
            }

            if (!state.Exists)
            {
                // The trust line is created on the way in, and the receiver owns it.
                state.Exists = true;
                AdjustOwnerCount(to, 1);
            }
        }

        private Holding XrpForWrite(string account)
        {
            if (!_xrp.TryGetValue(account, out Holding holding))
                _xrp[account] = holding = ReadXrp(account).Copy();

            return holding;
        }

        private LineState LineForWrite(LineKey line)
        {
            if (!_lines.TryGetValue(line, out LineState state))
                _lines[line] = state = ReadLine(line).Copy();

            return state;
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

            return Read(HoldingKey.Of(pool.Account, asset)).Current.WithAsset(asset);
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
            if (!World.Accounts.ContainsKey(account))
                return XrplAmount.Zero(XrpAsset);

            long ownerCount = Math.Max(0, ReserveOwnerCount(account) + ownerCountAdjustment);
            decimal reserve = World.Snapshot.ReserveBase + (decimal)World.Snapshot.ReserveIncrement * ownerCount;
            XrplAmount balance = Spendable(HoldingKey.Of(account, XrpAsset));
            decimal drops = balance.StMantissa;
            if (drops < reserve)
                return XrplAmount.Zero(XrpAsset);

            return XrplAmount.FromUnits(XrpAsset, AmountKind.Xrp, (long)(drops - reserve));
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

            LineInfo info = LineInfo(account, asset.Issuer, asset.Currency);
            return info != null && info.Side(asset.Issuer).Freeze;
        }

        /// <summary><c>isDeepFrozen</c>.</summary>
        internal bool IsDeepFrozen(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) != AmountKind.Iou || string.Equals(account, asset.Issuer, StringComparison.Ordinal))
                return false;

            LineInfo info = LineInfo(account, asset.Issuer, asset.Currency);
            return info != null && info.DeepFrozen;
        }

        /// <summary><c>accountHolds</c> with <c>ZeroIfFrozen</c>.</summary>
        internal XrplAmount AccountHolds(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Xrp)
                return XrpLiquid(account);

            HoldingKey key = HoldingKey.Of(account, asset);
            if (!Read(key).Exists || IsFrozen(account, asset) || IsDeepFrozen(account, asset))
                return XrplAmount.Zero(asset);

            return Spendable(key).WithAsset(asset);
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
