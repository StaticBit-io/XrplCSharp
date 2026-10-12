using System;
using System.Collections.Generic;

using Xrpl.Sugar;
using Xrpl.Utils.Hashes;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// Who holds what: an account and an asset, XRP keyed by an empty issuer, an MPT by its
    /// issuance ID in <see cref="Currency"/> and its issuer.
    /// </summary>
    internal readonly record struct HoldingKey(string Account, string Currency, string Issuer, bool IsMpt = false)
    {
        internal static HoldingKey Of(string account, IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) switch
            {
                AmountKind.Xrp => new HoldingKey(account, "XRP", string.Empty),
                AmountKind.Mpt => OfMpt(account, asset.MptIssuanceId),
                _ => new HoldingKey(account, asset.Currency, asset.Issuer),
            };

        internal static HoldingKey OfMpt(string account, string mptIssuanceId) =>
            new HoldingKey(account, DexAssets.MptKey(mptIssuanceId), DexAssets.MptIssuer(mptIssuanceId), true);

        /// <summary>The asset held.</summary>
        internal IssuedCurrency Asset =>
            IsMpt ? DexAssets.Mpt(Currency)
            : Issuer.Length == 0 ? new IssuedCurrency { Currency = "XRP" }
            : new IssuedCurrency { Currency = Currency, Issuer = Issuer };
    }

    /// <summary>
    /// The order books a snapshot covers, keyed by the asset taken in, the asset given out and
    /// the permissioned domain - empty for the open book.
    /// </summary>
    internal readonly record struct BookKey(string InCurrency, string InIssuer, string OutCurrency, string OutIssuer, string Domain)
    {
        internal static BookKey Of(IssuedCurrency @in, IssuedCurrency @out, string domain = null)
        {
            HoldingKey i = HoldingKey.Of(string.Empty, @in);
            HoldingKey o = HoldingKey.Of(string.Empty, @out);
            return new BookKey(i.Currency, i.Issuer, o.Currency, o.Issuer, DomainKey(domain));
        }

        /// <summary>A domain id as book keys compare it: upper-case hex, empty for the open book.</summary>
        internal static string DomainKey(string domain) =>
            string.IsNullOrEmpty(domain) ? string.Empty : domain.ToUpperInvariant();
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
    internal sealed record LineSide
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

        /// <summary>Whether the line counts toward this account's owner count.</summary>
        internal bool Reserve { get; init; }

        /// <summary>
        /// Whether this side holds nothing but defaults, so the line may stop counting toward the
        /// account's reserve once its balance is gone: no limit, no qualities, no freeze, and
        /// NoRipple as the account's <c>DefaultRipple</c> implies.
        /// </summary>
        internal bool AtDefaults(bool defaultRipple) =>
            NoRipple != defaultRipple && !Freeze && (Limit == null || Limit.Value.IsZero) && QualityIn == 0 && QualityOut == 0;
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
                    Reserve = line.Reserve,
                };
                LineSide peerSide = new LineSide
                {
                    Limit = line.PeerLimit,
                    QualityIn = line.PeerQualityIn,
                    QualityOut = line.PeerQualityOut,
                    NoRipple = line.PeerNoRipple,
                    Freeze = line.Frozen,
                    Auth = line.PeerAuthorized,
                    Reserve = line.PeerReserve,
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

            foreach (DexMptIssuance issuance in snapshot.MptIssuances ?? Array.Empty<DexMptIssuance>())
            {
                if (issuance?.MptIssuanceId != null)
                    MptIssuances[DexAssets.MptKey(issuance.MptIssuanceId)] = issuance;
            }

            foreach (DexMptHolding holding in snapshot.MptHoldings ?? Array.Empty<DexMptHolding>())
            {
                if (holding?.Account == null || holding.MptIssuanceId == null)
                    continue;

                (string, string) key = (holding.Account, DexAssets.MptKey(holding.MptIssuanceId));
                if (MptHoldings.ContainsKey(key))
                    throw new ArgumentException($"The MPT holding of {holding.Account} in {holding.MptIssuanceId} is listed twice.");

                MptHoldings[key] = holding;
            }

            foreach (DexOffer offer in snapshot.Offers ?? Array.Empty<DexOffer>())
            {
                if (offer == null)
                    continue;

                if (offer.Hybrid && string.IsNullOrEmpty(offer.DomainId))
                    throw new ArgumentException($"The hybrid offer {offer.Index} names no domain.");

                // Crossing, the taker pays what the offer asks for and gets what it gives. A domain
                // offer sits in its domain's book; a hybrid one in the open book too.
                if (string.IsNullOrEmpty(offer.DomainId) || offer.Hybrid)
                    AddToBook(BookKey.Of(offer.TakerPays.Asset, offer.TakerGets.Asset), offer);
                if (!string.IsNullOrEmpty(offer.DomainId))
                    AddToBook(BookKey.Of(offer.TakerPays.Asset, offer.TakerGets.Asset, offer.DomainId), offer);

                Offers[offer.Index] = offer;
            }

            // Directories in quality order, better first; within one, the order of the snapshot.
            foreach (List<DexOffer> list in Books.Values)
                StableSortByQuality(list);

            foreach (DexAmmPool pool in snapshot.Pools ?? Array.Empty<DexAmmPool>())
            {
                if (pool?.Account == null)
                    continue;
                AmmOfferMath.RequireTradingFee(pool.TradingFee, nameof(snapshot));
                AmmOfferMath.RequireTradingFee(pool.DiscountedFee, nameof(snapshot));

                Pools.Add(pool);
                PoolAccounts.Add(pool.Account);
                foreach (XrplAmount held in new[] { pool.Balance, pool.Balance2 })
                {
                    if (held.Kind == AmountKind.Mpt)
                    {
                        // The pool's balance is what its account's MPToken holds.
                        (string, string) holdingKey = (pool.Account, DexAssets.MptKey(held.Asset.MptIssuanceId));
                        MptHoldings.TryGetValue(holdingKey, out DexMptHolding listed);
                        MptHoldings[holdingKey] = new DexMptHolding
                        {
                            Account = pool.Account,
                            MptIssuanceId = holdingKey.Item2,
                            Amount = held.StMantissa,
                            Locked = listed?.Locked ?? false,
                            Authorized = true,
                        };
                        continue;
                    }

                    if (held.Kind != AmountKind.Iou)
                        continue;

                    // The pool's balance is what its account holds on the line to the issuer.
                    LineKey key = LineKey.Of(pool.Account, held.Asset.Issuer, held.Asset.Currency);
                    Lines[key] = new LineInfo { Key = key, Balance = FirstView(key, pool.Account, held) };
                }
            }

            foreach (DexDomain domain in snapshot.Domains ?? Array.Empty<DexDomain>())
            {
                if (domain?.DomainId != null)
                    Domains[BookKey.DomainKey(domain.DomainId)] = domain;
            }

            foreach (DexCredential credential in snapshot.Credentials ?? Array.Empty<DexCredential>())
            {
                if (credential?.Subject == null || credential.Issuer == null || credential.CredentialType == null)
                    continue;

                (string Subject, string Issuer, string Type) key = CredentialKey(credential.Subject, credential.Issuer, credential.CredentialType);
                Credentials[key] = credential;
                CredentialsById[Hashes.HashCredential(credential.Subject, credential.Issuer, key.Type)] = credential;
            }

            foreach (DexBook book in snapshot.PartialBooks ?? Array.Empty<DexBook>())
            {
                if (book?.TakerPays != null && book.TakerGets != null)
                    PartialBooks.Add(BookKey.Of(book.TakerPays, book.TakerGets, book.DomainId));
            }
        }

        private void AddToBook(BookKey book, DexOffer offer)
        {
            if (!Books.TryGetValue(book, out List<DexOffer> list))
                Books[book] = list = new List<DexOffer>();

            list.Add(offer);
        }

        private static (string Subject, string Issuer, string Type) CredentialKey(string subject, string issuer, string type) =>
            (subject, issuer, type.ToUpperInvariant());

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

        /// <summary>The MPT issuances by upper-case issuance ID.</summary>
        internal Dictionary<string, DexMptIssuance> MptIssuances { get; } = new Dictionary<string, DexMptIssuance>(StringComparer.Ordinal);

        /// <summary>The MPTokens by holder and upper-case issuance ID, the pools' among them.</summary>
        internal Dictionary<(string Account, string Id), DexMptHolding> MptHoldings { get; } = new Dictionary<(string Account, string Id), DexMptHolding>();

        /// <summary>The issuance of an MPT; null when the snapshot does not hold it, as for one that does not exist.</summary>
        internal DexMptIssuance Issuance(IssuedCurrency mpt) =>
            MptIssuances.TryGetValue(DexAssets.MptKey(mpt.MptIssuanceId), out DexMptIssuance issuance) ? issuance : null;

        /// <summary>
        /// <c>transferRate(view, MPTID)</c>: 1,000,000,000 plus 10,000 per unit of the issuance's
        /// <c>TransferFee</c>; <see cref="OfferCrossing.QualityOne"/> for none.
        /// </summary>
        internal uint MptTransferRate(IssuedCurrency mpt)
        {
            DexMptIssuance issuance = Issuance(mpt);
            return issuance == null || issuance.TransferFee == 0
                ? OfferCrossing.QualityOne
                : OfferCrossing.QualityOne + 10_000u * issuance.TransferFee;
        }

        internal Dictionary<string, DexDomain> Domains { get; } = new Dictionary<string, DexDomain>(StringComparer.Ordinal);

        internal Dictionary<(string Subject, string Issuer, string Type), DexCredential> Credentials { get; } =
            new Dictionary<(string Subject, string Issuer, string Type), DexCredential>();

        /// <summary>The credentials by ledger index, as a payment's <c>CredentialIDs</c> name them.</summary>
        internal Dictionary<string, DexCredential> CredentialsById { get; } = new Dictionary<string, DexCredential>(StringComparer.OrdinalIgnoreCase);

        internal HashSet<BookKey> PartialBooks { get; } = new HashSet<BookKey>();

        /// <summary>Whether a book step ran out of offers in a book the snapshot holds only in part.</summary>
        internal bool ReachedPartialBook { get; set; }

        /// <summary>Whether the engine's passes are recorded in <see cref="Passes"/>: for the transaction evaluated, not for a path search's trial runs.</summary>
        internal bool RecordPasses { get; set; }

        /// <summary>The strands the engine tried, pass by pass, over every flow run against this world.</summary>
        internal List<FlowPass> Passes { get; } = new List<FlowPass>();

        internal IReadOnlyList<DexOffer> Book(IssuedCurrency @in, IssuedCurrency @out, string domain = null) =>
            Books.TryGetValue(BookKey.Of(@in, @out, domain), out List<DexOffer> list) ? list : Array.Empty<DexOffer>();

        /// <summary>Whether the account is a pseudo-account: an AMM's, in this model.</summary>
        internal bool IsPseudoAccount(string account) => account != null && PoolAccounts.Contains(account);

        /// <summary>The credential <paramref name="subject"/> holds of this kind; null when it holds none.</summary>
        internal DexCredential Credential(string subject, string issuer, string type) =>
            Credentials.TryGetValue(CredentialKey(subject, issuer, type), out DexCredential credential) ? credential : null;

        /// <summary><c>credentials::checkExpired</c>: a credential is expired once the parent ledger closed after its expiration.</summary>
        internal bool Expired(DexCredential credential) =>
            credential.Expiration is { } expiration && Snapshot.ParentCloseTime > expiration;

        /// <summary><c>permissioned_dex::accountInDomain</c>: the owner, or a holder of an accepted, unexpired credential the domain accepts.</summary>
        internal bool AccountInDomain(string account, string domainId)
        {
            if (!Domains.TryGetValue(BookKey.DomainKey(domainId), out DexDomain domain))
                return false;
            if (string.Equals(domain.Owner, account, StringComparison.Ordinal))
                return true;

            foreach (DexCredentialType accepted in domain.AcceptedCredentials ?? Array.Empty<DexCredentialType>())
            {
                DexCredential credential = Credential(account, accepted.Issuer, accepted.CredentialType);
                if (credential != null && credential.Accepted && !Expired(credential))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// <c>credentials::validDomain</c> with the owner let through, as a transaction's checks
        /// run it: null for a member, <c>tecEXPIRED</c> when only expired credentials match, and
        /// <c>tecNO_AUTH</c> or <c>tecOBJECT_NOT_FOUND</c> otherwise.
        /// </summary>
        /// <param name="domainId">The domain.</param>
        /// <param name="subject">The account checked.</param>
        /// <param name="ownerIsMember">
        /// Whether the owner passes without a credential, as the transactions' checks let it;
        /// false for <c>credentials::validDomain</c> itself, as an MPT's <c>requireAuth</c> calls it.
        /// </param>
        internal string ValidDomain(string domainId, string subject, bool ownerIsMember = true)
        {
            if (!Domains.TryGetValue(BookKey.DomainKey(domainId), out DexDomain domain))
                return "tecOBJECT_NOT_FOUND";
            if (ownerIsMember && string.Equals(domain.Owner, subject, StringComparison.Ordinal))
                return null;

            bool foundExpired = false;
            foreach (DexCredentialType accepted in domain.AcceptedCredentials ?? Array.Empty<DexCredentialType>())
            {
                DexCredential credential = Credential(subject, accepted.Issuer, accepted.CredentialType);
                if (credential == null)
                    continue;
                if (Expired(credential))
                {
                    foundExpired = true;
                    continue;
                }

                if (credential.Accepted)
                    return null;
            }

            return foundExpired ? "tecEXPIRED" : "tecNO_AUTH";
        }

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

        /// <summary>The first account's settings, its reserve flag as the transaction left it.</summary>
        internal LineSide First { get; set; } = LineSide.Default;

        internal LineSide Second { get; set; } = LineSide.Default;

        internal bool DeepFrozen { get; set; }

        internal LineState Copy() =>
            new LineState
            {
                Balance = Balance,
                FirstDebits = FirstDebits,
                SecondDebits = SecondDebits,
                Exists = Exists,
                First = First,
                Second = Second,
                DeepFrozen = DeepFrozen,
            };
    }

    /// <summary>An account's owner count while a crossing runs, and the largest it has been (<c>ownerCountHook</c>).</summary>
    internal sealed class OwnerCounts
    {
        internal long Actual { get; set; }

        internal long Max { get; set; }

        internal OwnerCounts Copy() => new OwnerCounts { Actual = Actual, Max = Max };
    }

    /// <summary>An MPToken while the transaction runs, and what its holder has sent since it began.</summary>
    internal sealed class MptHoldingState
    {
        internal long Amount { get; set; }

        /// <summary><c>DeferredCredits</c>' debits of the holder (<c>creditMPT</c>).</summary>
        internal long Debits { get; set; }

        internal bool Exists { get; set; }

        /// <summary>Whether the transaction created the MPToken (<c>checkCreateMPT</c>).</summary>
        internal bool Created { get; set; }

        internal bool Locked { get; set; }

        internal bool Authorized { get; set; }

        internal MptHoldingState Copy() => (MptHoldingState)MemberwiseClone();
    }

    /// <summary>An MPT issuance while the transaction runs, and what its issuer has issued and given of its own since it began.</summary>
    internal sealed class IssuanceState
    {
        internal ulong Outstanding { get; set; }

        /// <summary><c>DeferredCredits</c>' issuer credit: what the issuer sent to holders.</summary>
        internal long Credit { get; set; }

        /// <summary>What the issuer's own offers gave (<c>issuerSelfDebitMPT</c>).</summary>
        internal long SelfDebit { get; set; }

        internal IssuanceState Copy() => (IssuanceState)MemberwiseClone();
    }

    /// <summary>What <c>requireAuth</c> asks of an MPToken (<c>AuthType</c>).</summary>
    internal enum MptAuth
    {
        /// <summary>A holder needs an MPToken, as <c>Legacy</c> does for an MPT.</summary>
        Strong,

        /// <summary>A missing MPToken passes; it is created when the MPT arrives.</summary>
        Weak,
    }

    /// <summary>What is left of an offer while a crossing runs, and what was taken of it.</summary>
    internal sealed class OfferState
    {
        internal XrplAmount TakerPays { get; set; }

        internal XrplAmount TakerGets { get; set; }

        /// <summary>What the offer received: the sum of what was taken of its <c>TakerPays</c>.</summary>
        internal XrplAmount FilledTakerPays { get; set; }

        /// <summary>What the offer gave: the sum of what was taken of its <c>TakerGets</c>.</summary>
        internal XrplAmount FilledTakerGets { get; set; }

        internal bool Deleted { get; set; }

        internal OfferRemovalReason? Reason { get; set; }

        internal OfferState Copy() => new OfferState
        {
            TakerPays = TakerPays,
            TakerGets = TakerGets,
            FilledTakerPays = FilledTakerPays,
            FilledTakerGets = FilledTakerGets,
            Deleted = Deleted,
            Reason = Reason,
        };
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
        private readonly Dictionary<(string Account, string Id), MptHoldingState> _mpts = new Dictionary<(string Account, string Id), MptHoldingState>();
        private readonly Dictionary<string, IssuanceState> _issuances = new Dictionary<string, IssuanceState>(StringComparer.Ordinal);
        private readonly List<FillRecord> _fills = new List<FillRecord>();
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
            foreach (KeyValuePair<(string Account, string Id), MptHoldingState> entry in _mpts)
                parent._mpts[entry.Key] = entry.Value.Copy();
            foreach (KeyValuePair<string, IssuanceState> entry in _issuances)
                parent._issuances[entry.Key] = entry.Value.Copy();

            // Moved, not copied: a fill belongs to one view of a chain.
            parent._fills.AddRange(_fills);
            _fills.Clear();
        }

        /// <summary>A fill this view took, before its pass and strand are known.</summary>
        internal void RecordFill(FillRecord fill) => _fills.Add(fill);

        /// <summary>The fills this view recorded and has not yet passed on.</summary>
        internal IReadOnlyList<FillRecord> OwnFills => _fills;

        /// <summary>Every fill of this view and its parents, oldest first.</summary>
        internal List<FillRecord> AllFills()
        {
            List<List<FillRecord>> layers = new List<List<FillRecord>>();
            for (DexView view = this; view != null; view = view._parent)
                layers.Add(view._fills);

            List<FillRecord> all = new List<FillRecord>();
            for (int i = layers.Count - 1; i >= 0; i--)
                all.AddRange(layers[i]);

            return all;
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

                // An issuer's "balance" of its own MPT is the negated OutstandingAmount.
                foreach ((string account, string id) in view._mpts.Keys)
                    keys.Add(HoldingKey.OfMpt(account, id));
                foreach (string id in view._issuances.Keys)
                    keys.Add(HoldingKey.OfMpt(DexAssets.MptIssuer(id), id));
            }

            return keys;
        }

        /// <summary>The MPTokens this view or a parent has created, as holder and issuance.</summary>
        internal IEnumerable<(string Account, string Id)> CreatedMpTokens()
        {
            HashSet<(string, string)> created = new HashSet<(string, string)>();
            for (DexView view = this; view != null; view = view._parent)
            {
                foreach (KeyValuePair<(string Account, string Id), MptHoldingState> entry in view._mpts)
                {
                    if (ReadMpt(entry.Key.Account, entry.Key.Id).Created)
                        created.Add(entry.Key);
                }
            }

            return created;
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
            return new OfferState
            {
                TakerPays = offer.TakerPays,
                TakerGets = offer.TakerGets,
                FilledTakerPays = XrplAmount.Zero(offer.TakerPays.Asset),
                FilledTakerGets = XrplAmount.Zero(offer.TakerGets.Asset),
            };
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
            state.FilledTakerPays = StepMath.Add(state.FilledTakerPays, consumedIn, Rules);
            state.FilledTakerGets = StepMath.Add(state.FilledTakerGets, consumedOut, Rules);
        }

        /// <summary><c>offerDelete</c>: the offer leaves the book and its owner owns one object less.</summary>
        internal void DeleteOffer(string index, OfferRemovalReason reason)
        {
            OfferState state = OfferForWrite(index);
            if (state.Deleted)
                return;

            state.Deleted = true;
            state.Reason = reason;
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
            if (key.IsMpt)
                return MptHoldingOf(key, ReadMpt(key.Account, key.Currency), ReadIssuance(key.Currency));

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
            if (key.IsMpt)
                return MptHoldingOf(key, InitialMpt(key.Account, key.Currency), InitialIssuance(key.Currency));

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
                ? new LineState
                {
                    Balance = info.Balance,
                    FirstDebits = zero,
                    SecondDebits = zero,
                    Exists = true,
                    First = info.First,
                    Second = info.Second,
                    DeepFrozen = info.DeepFrozen,
                }
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

        /// <summary>The line's settings as they stand now; null when there is no such line.</summary>
        internal LineInfo LineInfo(string a, string b, string currency)
        {
            if (string.Equals(a, b, StringComparison.Ordinal))
                return null;

            LineKey key = LineKey.Of(a, b, currency);
            LineState state = ReadLine(key);
            return state.Exists
                ? new LineInfo { Key = key, First = state.First, Second = state.Second, DeepFrozen = state.DeepFrozen, Balance = state.Balance }
                : null;
        }

        /// <summary>Whether the line existed before the transaction.</summary>
        internal bool LineExistedBefore(LineKey key) => InitialLine(key).Exists;

        /// <summary>The trust lines this view or a parent has written.</summary>
        internal IEnumerable<LineKey> TouchedLines()
        {
            HashSet<LineKey> keys = new HashSet<LineKey>();
            for (DexView view = this; view != null; view = view._parent)
                keys.UnionWith(view._lines.Keys);

            return keys;
        }

        /// <summary>Whether the line exists now.</summary>
        internal bool LineExistsNow(LineKey key) => ReadLine(key).Exists;

        /// <summary>
        /// <c>accountSend</c> without a transfer fee: <c>rippleCredit</c> on the trust line between
        /// the two accounts for an issued currency, or XRP between an account and the empty account.
        /// </summary>
        internal void Send(string from, string to, XrplAmount amount)
        {
            if (amount.IsZero || string.Equals(from, to, StringComparison.Ordinal))
                return;

            if (amount.Kind == AmountKind.Mpt)
            {
                if (SendMpt(from, to, amount) is { } failed)
                    throw new FlowFailedException(failed, "MPT send failed.");
                return;
            }

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
            bool fromFirst = line.IsFirst(from);
            if (!state.Exists)
            {
                // trustCreate: the receiver owns the new line, which ripples as each account's
                // DefaultRipple says.
                state.Exists = true;
                state.DeepFrozen = false;
                LineSide receiver = new LineSide { Reserve = true, NoRipple = !DefaultRipple(to) };
                LineSide sender = new LineSide { NoRipple = !DefaultRipple(from) };
                state.First = fromFirst ? sender : receiver;
                state.Second = fromFirst ? receiver : sender;
                AdjustOwnerCount(to, 1);
            }

            // The sender's view of the balance, before and after.
            XrplAmount before = fromFirst ? state.Balance : -state.Balance;
            if (fromFirst)
            {
                state.Balance = StepMath.Subtract(state.Balance, moved, Rules);
                state.FirstDebits = StepMath.Add(state.FirstDebits, moved, Rules);
            }
            else
            {
                state.Balance = StepMath.Add(state.Balance, moved, Rules);
                state.SecondDebits = StepMath.Add(state.SecondDebits, moved, Rules);
            }

            XrplAmount after = fromFirst ? state.Balance : -state.Balance;
            ReleaseReserve(state, from, fromFirst, before, after);
        }

        /// <summary>
        /// <c>directSendNoFeeIOU</c>'s cleanup: a sender whose positive balance is gone, and whose
        /// side of the line holds nothing but defaults, stops paying the line's reserve; the line
        /// is deleted when its balance is zero and the receiver does not pay a reserve for it either.
        /// </summary>
        private void ReleaseReserve(LineState state, string from, bool fromFirst, XrplAmount before, XrplAmount after)
        {
            LineSide sender = fromFirst ? state.First : state.Second;
            LineSide receiver = fromFirst ? state.Second : state.First;
            if (!StepMath.IsPositive(before) || StepMath.IsPositive(after) || !sender.Reserve || !sender.AtDefaults(DefaultRipple(from)))
                return;

            AdjustOwnerCount(from, -1);
            sender = sender with { Reserve = false };
            if (fromFirst)
                state.First = sender;
            else
                state.Second = sender;

            if (after.IsZero && !receiver.Reserve)
                state.Exists = false;
        }

        private bool DefaultRipple(string account) =>
            World.Accounts.TryGetValue(account, out DexAccount dex) && dex.DefaultRipple;

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
            if (frozen || (DexAssets.IsMpt(asset) && IsFrozen(pool.Account, asset)))
                return XrplAmount.Zero(asset);

            return Read(HoldingKey.Of(pool.Account, asset)).Current.WithAsset(asset);
        }

        // ---- MPTs ----

        private static Holding MptHoldingOf(HoldingKey key, MptHoldingState holding, IssuanceState issuance)
        {
            IssuedCurrency asset = DexAssets.Mpt(key.Currency);
            XrplAmount zero = XrplAmount.Zero(asset);
            if (string.Equals(key.Account, key.Issuer, StringComparison.Ordinal))
            {
                // The issuer holds no MPToken; what it has issued is its debt.
                return new Holding { Current = XrplAmount.FromUnits(asset, AmountKind.Mpt, -checked((long)issuance.Outstanding)), Debits = zero, Exists = true };
            }

            return new Holding
            {
                Current = XrplAmount.FromUnits(asset, AmountKind.Mpt, holding.Amount),
                Debits = XrplAmount.FromUnits(asset, AmountKind.Mpt, holding.Debits),
                Exists = holding.Exists,
            };
        }

        private MptHoldingState ReadMpt(string account, string id)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._mpts.TryGetValue((account, id), out MptHoldingState state))
                    return state;
            }

            return InitialMpt(account, id);
        }

        private MptHoldingState InitialMpt(string account, string id) =>
            World.MptHoldings.TryGetValue((account, id), out DexMptHolding holding)
                ? new MptHoldingState { Amount = checked((long)holding.Amount), Exists = true, Locked = holding.Locked, Authorized = holding.Authorized }
                : new MptHoldingState();

        private IssuanceState ReadIssuance(string id)
        {
            for (DexView view = this; view != null; view = view._parent)
            {
                if (view._issuances.TryGetValue(id, out IssuanceState state))
                    return state;
            }

            return InitialIssuance(id);
        }

        private IssuanceState InitialIssuance(string id) =>
            new IssuanceState { Outstanding = World.MptIssuances.TryGetValue(id, out DexMptIssuance issuance) ? issuance.OutstandingAmount : 0 };

        private MptHoldingState MptForWrite(string account, string id)
        {
            if (!_mpts.TryGetValue((account, id), out MptHoldingState state))
                _mpts[(account, id)] = state = ReadMpt(account, id).Copy();

            return state;
        }

        private IssuanceState IssuanceForWrite(string id)
        {
            if (!_issuances.TryGetValue(id, out IssuanceState state))
                _issuances[id] = state = ReadIssuance(id).Copy();

            return state;
        }

        /// <summary><c>maxMPTAmount</c>: the issuance's <c>MaximumAmount</c>, or the protocol's maximum.</summary>
        private static long MaxMptAmount(DexMptIssuance issuance) =>
            checked((long)(issuance.MaximumAmount ?? XrplAmount.MaxMptAmount));

        /// <summary><c>maxMPTAmount</c> of the MPT's issuance; zero when the snapshot does not hold it.</summary>
        internal long MaxMptAmount(IssuedCurrency mpt) =>
            World.Issuance(mpt) is { } issuance ? MaxMptAmount(issuance) : 0;

        /// <summary><c>availableMPTAmount</c>: what the issuer may still issue now.</summary>
        private long Available(DexMptIssuance issuance) =>
            MaxMptAmount(issuance) - checked((long)ReadIssuance(DexAssets.MptKey(issuance.MptIssuanceId)).Outstanding);

        /// <summary><c>availableMPTAmount</c> before the transaction.</summary>
        private static long AvailableBefore(DexMptIssuance issuance) =>
            MaxMptAmount(issuance) - checked((long)issuance.OutstandingAmount);

        /// <summary><c>holdingExists</c>: the issuer always holds its own MPT; anyone else through an MPToken.</summary>
        internal bool MptHoldingExists(string account, IssuedCurrency mpt)
        {
            string id = DexAssets.MptKey(mpt.MptIssuanceId);
            return string.Equals(account, DexAssets.MptIssuer(id), StringComparison.Ordinal) || ReadMpt(account, id).Exists;
        }

        /// <summary>Whether the account has an MPToken of the MPT now; the issuer never has one.</summary>
        internal bool MpTokenExists(string account, IssuedCurrency mpt) => ReadMpt(account, DexAssets.MptKey(mpt.MptIssuanceId)).Exists;

        /// <summary><c>isGlobalFrozen</c> of an MPT: its issuance is locked.</summary>
        internal bool IsMptGlobalLocked(IssuedCurrency mpt) => World.Issuance(mpt)?.Locked == true;

        /// <summary><c>isIndividualFrozen</c> of an MPT: the account's MPToken is locked.</summary>
        internal bool IsMptIndividualLocked(string account, IssuedCurrency mpt)
        {
            MptHoldingState holding = ReadMpt(account, DexAssets.MptKey(mpt.MptIssuanceId));
            return holding.Exists && holding.Locked;
        }

        /// <summary>
        /// <c>accountHolds</c> of an MPT. The issuer, asked for its full balance, has what it may
        /// still issue; a holder what its MPToken holds - nothing when it is locked or unauthorized,
        /// as asked - and under <c>MPTokensV2</c> less what it received during the transaction
        /// (<c>balanceHookMPT</c>).
        /// </summary>
        internal XrplAmount AccountHoldsMpt(string account, IssuedCurrency mpt, bool zeroIfFrozen, bool zeroIfUnauthorized, bool fullBalance)
        {
            string id = DexAssets.MptKey(mpt.MptIssuanceId);
            IssuedCurrency asset = DexAssets.Mpt(id);
            DexMptIssuance issuance = World.Issuance(asset);
            if (fullBalance && string.Equals(account, DexAssets.MptIssuer(id), StringComparison.Ordinal))
            {
                if (issuance == null)
                    return XrplAmount.Zero(asset);

                long available = Available(issuance);
                if (!Rules.MPTokensV2)
                    return XrplAmount.FromUnits(asset, AmountKind.Mpt, available);

                // The issuer's orig balance is what it could issue before the transaction.
                long issued = ReadIssuance(id).Credit;
                return Hooked(asset, available, AvailableBefore(issuance), issued);
            }

            MptHoldingState holding = ReadMpt(account, id);
            long amount = 0;
            if (holding.Exists && !(zeroIfFrozen && IsFrozen(account, asset)))
            {
                amount = holding.Amount;
                if (zeroIfUnauthorized)
                {
                    bool unauthorized = Rules.LargeNumbers
                        ? RequireMptAuth(account, asset, MptAuth.Strong) != null
                        : issuance != null && issuance.RequireAuth && !holding.Authorized;
                    if (unauthorized)
                        amount = 0;
                }
            }

            if (!Rules.MPTokensV2)
                return XrplAmount.FromUnits(asset, AmountKind.Mpt, amount);

            return Hooked(asset, amount, InitialMpt(account, id).Amount, holding.Debits);
        }

        /// <summary><c>balanceHookMPT</c>: the balance less what was received during the transaction, never below zero.</summary>
        private static XrplAmount Hooked(IssuedCurrency asset, long amount, long before, long debits)
        {
            long adjusted = Math.Min(amount, Math.Min(before - debits, before));
            return adjusted > 0 ? XrplAmount.FromUnits(asset, AmountKind.Mpt, adjusted) : XrplAmount.Zero(asset);
        }

        /// <summary>
        /// <c>issuerFundsToSelfIssue</c>: what an issuer's own offers may still give of its MPT -
        /// what it could issue before the transaction less what those offers gave
        /// (<c>balanceHookSelfIssueMPT</c>).
        /// </summary>
        internal XrplAmount IssuerFundsToSelfIssue(IssuedCurrency mpt)
        {
            string id = DexAssets.MptKey(mpt.MptIssuanceId);
            IssuedCurrency asset = DexAssets.Mpt(id);
            if (World.Issuance(asset) is not { } issuance)
                return XrplAmount.Zero(asset);

            long left = AvailableBefore(issuance) - ReadIssuance(id).SelfDebit;
            return left > 0 ? XrplAmount.FromUnits(asset, AmountKind.Mpt, left) : XrplAmount.Zero(asset);
        }

        /// <summary><c>issuerSelfDebitHookMPT</c>: an issuer's own offer gave <paramref name="amount"/> of its MPT.</summary>
        internal void IssuerSelfDebit(IssuedCurrency mpt, XrplAmount amount)
        {
            IssuanceState state = IssuanceForWrite(DexAssets.MptKey(mpt.MptIssuanceId));
            state.SelfDebit = checked(state.SelfDebit + (long)amount.StMantissa);
        }

        /// <summary>
        /// <c>checkCreateMPT</c>: the MPToken an account needs to receive the MPT, created with
        /// its flags clear and one more object for its owner when it has none.
        /// </summary>
        internal void CreateMpToken(string account, IssuedCurrency mpt)
        {
            string id = DexAssets.MptKey(mpt.MptIssuanceId);
            if (string.Equals(account, DexAssets.MptIssuer(id), StringComparison.Ordinal) || ReadMpt(account, id).Exists)
                return;

            MptHoldingState holding = MptForWrite(account, id);
            holding.Exists = true;
            holding.Created = true;
            holding.Amount = 0;
            holding.Locked = false;
            holding.Authorized = false;
            AdjustOwnerCount(account, 1);
        }

        /// <summary>
        /// <c>directSendNoFeeMPT</c>: the MPT moves between the issuer and a holder, the issuance's
        /// <c>OutstandingAmount</c> following it. Null on success, otherwise the failure:
        /// <c>tecPATH_DRY</c> when issuing would overflow, <c>tecINSUFFICIENT_FUNDS</c> when the
        /// holder holds too little, <c>tecNO_AUTH</c> when a holder has no MPToken.
        /// </summary>
        internal string SendMpt(string from, string to, XrplAmount amount)
        {
            string id = DexAssets.MptKey(amount.Asset.MptIssuanceId);
            if (World.MptIssuances.TryGetValue(id, out DexMptIssuance issuance) == false)
                return "tecOBJECT_NOT_FOUND";

            string issuer = DexAssets.MptIssuer(id);
            long amt = checked((long)amount.StMantissa);
            long maximum = MaxMptAmount(issuance);
            IssuanceState state = IssuanceForWrite(id);
            ulong outstanding = state.Outstanding;

            if (string.Equals(from, issuer, StringComparison.Ordinal))
            {
                // AllowMPTOverflow::Yes: OutstandingAmount may pass MaximumAmount for a while.
                if (Rules.MPTokensV2 && (amt > maximum || outstanding > ulong.MaxValue - (ulong)amt))
                    return "tecPATH_DRY";

                state.Outstanding = outstanding + (ulong)amt;
            }
            else
            {
                MptHoldingState sender = MptForWrite(from, id);
                if (!sender.Exists)
                    return "tecNO_AUTH";
                if (sender.Amount < amt)
                    return "tecINSUFFICIENT_FUNDS";

                CreditHook(from, issuer, id, amt, state);
                sender.Amount -= amt;
            }

            if (string.Equals(to, issuer, StringComparison.Ordinal))
            {
                if (outstanding < (ulong)amt)
                    return "tecINTERNAL";

                state.Outstanding = outstanding - (ulong)amt;
            }
            else
            {
                MptHoldingState receiver = MptForWrite(to, id);
                if (!receiver.Exists)
                    return "tecNO_AUTH";

                CreditHook(from, issuer, id, amt, state);
                receiver.Amount = checked(receiver.Amount + amt);
            }

            return null;
        }

        /// <summary><c>creditHookMPT</c>: the issuer's issues add to its credit, a holder's sends to its debits.</summary>
        private void CreditHook(string from, string issuer, string id, long amount, IssuanceState state)
        {
            if (string.Equals(from, issuer, StringComparison.Ordinal))
            {
                state.Credit = checked(state.Credit + amount);
                return;
            }

            MptHoldingState sender = MptForWrite(from, id);
            sender.Debits = checked(sender.Debits + amount);
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

        /// <summary>
        /// Whether the issuer or the line freezes <paramref name="account"/>'s holding
        /// (<c>isFrozen</c>); for an MPT, whether the issuance or the account's MPToken is locked.
        /// </summary>
        internal bool IsFrozen(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Mpt)
                return IsMptGlobalLocked(asset) || IsMptIndividualLocked(account, asset);
            if (XrplAmount.KindOf(asset) != AmountKind.Iou)
                return false;
            if (World.Accounts.TryGetValue(asset.Issuer, out DexAccount issuer) && issuer.GlobalFreeze)
                return true;
            if (string.Equals(account, asset.Issuer, StringComparison.Ordinal))
                return false;

            LineInfo info = LineInfo(account, asset.Issuer, asset.Currency);
            return info != null && info.Side(asset.Issuer).Freeze;
        }

        /// <summary><c>isDeepFrozen</c>: for an MPT, which a lock stops both ways, the same as <see cref="IsFrozen"/>.</summary>
        internal bool IsDeepFrozen(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Mpt)
                return IsFrozen(account, asset);
            if (XrplAmount.KindOf(asset) != AmountKind.Iou || string.Equals(account, asset.Issuer, StringComparison.Ordinal))
                return false;

            LineInfo info = LineInfo(account, asset.Issuer, asset.Currency);
            return info != null && info.DeepFrozen;
        }

        /// <summary>
        /// <c>requireAuth</c> with <c>AuthType::Legacy</c>: null when <paramref name="account"/>
        /// may hold <paramref name="asset"/>, otherwise <c>tecNO_LINE</c> or <c>tecNO_AUTH</c>.
        /// An issuer without <c>lsfRequireAuth</c> lets anyone hold its currency.
        /// </summary>
        internal string RequireAuth(string account, IssuedCurrency asset, MptAuth mptAuth = MptAuth.Strong)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Mpt)
                return RequireMptAuth(account, asset, mptAuth);
            if (XrplAmount.KindOf(asset) != AmountKind.Iou || string.Equals(account, asset.Issuer, StringComparison.Ordinal))
                return null;
            if (!World.Accounts.TryGetValue(asset.Issuer, out DexAccount issuer) || !issuer.RequireAuth)
                return null;

            LineInfo line = LineInfo(account, asset.Issuer, asset.Currency);
            if (line == null)
                return "tecNO_LINE";
            if (line.Side(asset.Issuer).Auth)
                return null;

            // A pseudo-account only holds assets for the object that owns it.
            if (Rules.FixCleanup3_4_0 && World.IsPseudoAccount(account))
                return null;

            return "tecNO_AUTH";
        }

        /// <summary>
        /// <c>requireAuth</c> of an MPT: null when <paramref name="account"/> may hold it, otherwise
        /// <c>tecOBJECT_NOT_FOUND</c> for an unknown issuance or <c>tecNO_AUTH</c> (or a domain's
        /// <c>tecEXPIRED</c>). The issuer is always authorized; a pseudo-account holds for its
        /// object. <see cref="MptAuth.Weak"/> lets a missing MPToken through.
        /// </summary>
        /// <remarks>Vault shares, whose issuer is a vault, are not modelled: their underlying asset's authorization is not checked.</remarks>
        internal string RequireMptAuth(string account, IssuedCurrency mpt, MptAuth authType)
        {
            DexMptIssuance issuance = World.Issuance(mpt);
            if (issuance == null)
                return "tecOBJECT_NOT_FOUND";

            string id = DexAssets.MptKey(mpt.MptIssuanceId);
            if (string.Equals(account, DexAssets.MptIssuer(id), StringComparison.Ordinal))
                return null;

            // SingleAssetVault or MPTokensV2 exempt a pseudo-account; fixCleanup3_3_0 does it first.
            bool exempt = Rules.LargeNumbers && World.IsPseudoAccount(account);
            if (Rules.FixCleanup3_3_0 && exempt)
                return null;

            MptHoldingState token = ReadMpt(account, id);
            if (!token.Exists && authType == MptAuth.Strong)
                return "tecNO_AUTH";

            if (!string.IsNullOrEmpty(issuance.DomainId))
            {
                string domain = World.ValidDomain(issuance.DomainId, account, ownerIsMember: false);
                if (domain == null || !token.Exists)
                    return domain;
            }

            if (!Rules.FixCleanup3_3_0 && exempt)
                return null;

            return issuance.RequireAuth && (!token.Exists || !token.Authorized) ? "tecNO_AUTH" : null;
        }

        /// <summary><c>canTrade</c>: null for anything but an MPT whose issuance lets it trade on the DEX.</summary>
        internal string CanTrade(IssuedCurrency asset)
        {
            if (!DexAssets.IsMpt(asset))
                return null;

            DexMptIssuance issuance = World.Issuance(asset);
            if (issuance == null)
                return "tecOBJECT_NOT_FOUND";

            return issuance.CanTrade ? null : "tecNO_PERMISSION";
        }

        /// <summary><c>canTransfer</c>: an MPT moves between holders only when its issuance allows it; to or from the issuer always.</summary>
        internal string CanTransfer(IssuedCurrency mpt, string from, string to)
        {
            DexMptIssuance issuance = World.Issuance(mpt);
            if (issuance == null)
                return "tecOBJECT_NOT_FOUND";

            string issuer = DexAssets.MptIssuer(mpt.MptIssuanceId);
            if (string.Equals(from, issuer, StringComparison.Ordinal) || string.Equals(to, issuer, StringComparison.Ordinal))
                return null;

            return issuance.CanTransfer ? null : "tecNO_AUTH";
        }

        /// <summary><c>accountHolds</c> with <c>ZeroIfFrozen</c> (and for an MPT <c>ZeroIfUnauthorized</c>).</summary>
        internal XrplAmount AccountHolds(string account, IssuedCurrency asset)
        {
            if (XrplAmount.KindOf(asset) == AmountKind.Mpt)
                return AccountHoldsMpt(account, asset, zeroIfFrozen: true, zeroIfUnauthorized: true, fullBalance: false);
            if (XrplAmount.KindOf(asset) == AmountKind.Xrp)
                return XrpLiquid(account);

            HoldingKey key = HoldingKey.Of(account, asset);
            if (!Read(key).Exists || IsFrozen(account, asset) || IsDeepFrozen(account, asset))
                return XrplAmount.Zero(asset);

            return Spendable(key).WithAsset(asset);
        }

        /// <summary>
        /// <c>accountFunds</c>: <paramref name="whenIssuer"/> for the issuer of an issued currency,
        /// what is left to issue for the issuer of an MPT, otherwise <see cref="AccountHolds"/>.
        /// </summary>
        internal XrplAmount AccountFunds(string account, XrplAmount whenIssuer)
        {
            if (whenIssuer.Kind == AmountKind.Mpt)
                return AccountHoldsMpt(account, whenIssuer.Asset, zeroIfFrozen: true, zeroIfUnauthorized: true, fullBalance: true);
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
