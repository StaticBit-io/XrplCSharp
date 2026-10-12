using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.AddressCodec;
using Xrpl.Models.Common;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// A path element as the path finder builds it (<c>STPathElement</c>): its type bits, and an
    /// account, a path asset - a currency, or an MPT's issuance ID - and an issuer that it keeps
    /// even where the type leaves them out of the path. The empty account or issuer stands for
    /// XRP's.
    /// </summary>
    internal readonly struct PfElement : IEquatable<PfElement>
    {
        internal const int TypeAccount = 0x01;
        internal const int TypeCurrency = 0x10;
        internal const int TypeIssuer = 0x20;
        internal const int TypeMpt = 0x40;

        private PfElement(int type, string account, string currency, string issuer)
        {
            Type = type;
            Account = account ?? string.Empty;
            Currency = currency;
            Issuer = issuer ?? string.Empty;
        }

        internal int Type { get; }

        internal string Account { get; }

        /// <summary>The path asset: a currency code, or an MPT's issuance ID (<see cref="PathCurrency.IsMpt"/>).</summary>
        internal string Currency { get; }

        internal string Issuer { get; }

        internal bool IsOffer => Account.Length == 0;

        internal bool IsAccount => !IsOffer;

        internal bool HasCurrency => (Type & TypeCurrency) != 0;

        internal bool HasMpt => (Type & TypeMpt) != 0;

        internal bool HasIssuer => (Type & TypeIssuer) != 0;

        /// <summary>
        /// The element with its type given (<c>STPathElement(uType, account, asset, issuer)</c>),
        /// the asset bit that does not fit the asset dropped.
        /// </summary>
        internal static PfElement Typed(int type, string account, string currency, string issuer) =>
            new PfElement(PathCurrency.IsMpt(currency) ? type & ~TypeCurrency : type & ~TypeMpt, account, currency, issuer);

        /// <summary>The element whose type follows from its fields (<c>STPathElement(account, asset, issuer)</c>).</summary>
        internal static PfElement Of(string account, string currency, string issuer)
        {
            int type = 0;
            if (!string.IsNullOrEmpty(account))
                type |= TypeAccount;
            if (currency != "XRP")
                type |= PathCurrency.IsMpt(currency) ? TypeMpt : TypeCurrency;
            if (!string.IsNullOrEmpty(issuer))
                type |= TypeIssuer;
            return new PfElement(type, account, currency, issuer);
        }

        /// <summary>The element as a path in a transaction carries it.</summary>
        internal PathElement ToPathElement()
        {
            if (IsAccount)
                return PathElement.OfAccount(Account);

            return new PathElement(null, HasCurrency ? Currency : null, HasIssuer ? Issuer : null, HasMpt ? Currency : null);
        }

        internal PathStep ToPathStep()
        {
            if (IsAccount)
                return new PathStep { Account = Account };

            return new PathStep
            {
                CurrencyCode = HasCurrency ? Currency : null,
                MPTokenIssuanceID = HasMpt ? Currency : null,
                Issuer = HasIssuer && Issuer.Length != 0 ? Issuer : null,
            };
        }

        /// <summary>rippled's equality: whether it is an account, and the account, currency and issuer.</summary>
        public bool Equals(PfElement other) =>
            (Type & TypeAccount) == (other.Type & TypeAccount) &&
            string.Equals(Account, other.Account, StringComparison.Ordinal) &&
            string.Equals(Currency, other.Currency, StringComparison.Ordinal) &&
            string.Equals(Issuer, other.Issuer, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PfElement other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Type & TypeAccount, Account, Currency, Issuer);

        internal string Key => $"{Type & TypeAccount}|{Account}|{Currency}|{Issuer}";
    }

    /// <summary>A path the finder builds (<c>STPath</c>).</summary>
    internal sealed class PfPath
    {
        private readonly List<PfElement> _elements;

        internal PfPath()
        {
            _elements = new List<PfElement>();
        }

        internal PfPath(PfPath other)
        {
            _elements = new List<PfElement>(other._elements);
        }

        internal int Count => _elements.Count;

        internal bool IsEmpty => _elements.Count == 0;

        internal PfElement this[int index]
        {
            get => _elements[index];
            set => _elements[index] = value;
        }

        internal PfElement Back => _elements[_elements.Count - 1];

        internal PfElement Front => _elements[0];

        internal void Add(PfElement element) => _elements.Add(element);

        /// <summary><c>STPath::hasSeen</c>: an element with this account, currency and issuer.</summary>
        internal bool HasSeen(string account, string currency, string issuer) =>
            _elements.Exists(e =>
                string.Equals(e.Account, account ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(e.Currency, currency, StringComparison.Ordinal) &&
                string.Equals(e.Issuer, issuer ?? string.Empty, StringComparison.Ordinal));

        internal string Key => string.Join(";", _elements.Select(e => e.Key));

        internal IReadOnlyList<PathElement> ToPathElements() => _elements.Select(e => e.ToPathElement()).ToList();

        internal List<PathStep> ToPathSteps() => _elements.Select(e => e.ToPathStep()).ToList();

        /// <summary>The path without its first element (<c>removeIssuer</c>).</summary>
        internal PfPath WithoutFirst()
        {
            PfPath result = new PfPath();
            for (int i = 1; i < _elements.Count; i++)
                result.Add(_elements[i]);
            return result;
        }
    }

    /// <summary>A set of paths that keeps the first of duplicates (<c>STPathSet</c> with <c>DeduplicationTag</c>).</summary>
    internal sealed class PfPathSet
    {
        private readonly List<PfPath> _paths = new List<PfPath>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        internal int Count => _paths.Count;

        internal PfPath this[int index] => _paths[index];

        internal IReadOnlyList<PfPath> Paths => _paths;

        internal bool Add(PfPath path)
        {
            if (!_seen.Add(path.Key))
                return false;

            _paths.Add(path);
            return true;
        }

        /// <summary><c>assembleAdd</c>: the base with the tail appended, unless that path is there already.</summary>
        internal bool AssembleAdd(PfPath basePath, PfElement tail)
        {
            PfPath combined = new PfPath(basePath);
            combined.Add(tail);
            return Add(combined);
        }
    }

    /// <summary>
    /// rippled's <c>AssetCache</c> for trust lines: an account's lines read once, those an
    /// "incoming" account cannot ripple through left out, and the full set served for both
    /// directions once it is read.
    /// </summary>
    internal sealed class LineCache
    {
        private readonly PathfindingSource _source;
        private readonly Dictionary<string, (bool Outgoing, List<DexTrustLine> Lines)> _lines =
            new Dictionary<string, (bool Outgoing, List<DexTrustLine> Lines)>(StringComparer.Ordinal);

        internal LineCache(PathfindingSource source)
        {
            _source = source;
        }

        /// <summary><c>getRippleLines</c>: null when the account has no such lines.</summary>
        internal async Task<List<DexTrustLine>> RippleLinesAsync(string account, bool outgoing, CancellationToken cancellationToken)
        {
            if (_lines.TryGetValue(account, out (bool Outgoing, List<DexTrustLine> Lines) cached))
            {
                // The outgoing set is the superset: it serves an incoming request too.
                if (cached.Outgoing == outgoing || cached.Outgoing)
                    return cached.Lines;
            }

            List<DexTrustLine> lines = new List<DexTrustLine>();
            foreach (DexTrustLine line in await _source.LinesAsync(account, cancellationToken).ConfigureAwait(false))
            {
                if (outgoing || !line.NoRipple)
                    lines.Add(line);
            }

            List<DexTrustLine> stored = lines.Count == 0 ? null : lines;
            _lines[account] = (outgoing, stored);
            return stored;
        }
    }

    /// <summary>
    /// rippled 3.4.1's <c>Pathfinder</c>: the candidate paths of a payment, built from a table of
    /// path shapes for the kind of payment - accounts, books, the book to XRP, the book to the
    /// delivered currency, the destination - then ranked by running each through the payment
    /// engine, and the best ones picked.
    /// </summary>
    internal sealed class Pathfinder
    {
        private const int MaxCompletePaths = 1000;
        private const int HighPriority = 10000;

        private const int AddAccounts = 0x001;
        private const int AddBooks = 0x002;
        private const int ObXrp = 0x010;
        private const int ObLast = 0x040;
        private const int AcLast = 0x080;

        private enum NodeType
        {
            Source,
            Accounts,
            Books,
            XrpBook,
            DestBook,
            Destination,
        }

        private enum PaymentType
        {
            XrpToXrp,
            XrpToNonXrp,
            NonXrpToXrp,
            NonXrpToSame,
            NonXrpToNonXrp,
        }

        /// <summary><c>initPathTable</c>: the path shapes of each kind of payment and the search level each needs.</summary>
        private static readonly Dictionary<PaymentType, (int Cost, string Path)[]> PathTable = new Dictionary<PaymentType, (int, string)[]>
        {
            [PaymentType.XrpToXrp] = Array.Empty<(int, string)>(),
            [PaymentType.XrpToNonXrp] = new[] { (1, "sfd"), (3, "sfad"), (5, "sfaad"), (6, "sbfd"), (8, "sbafd"), (9, "sbfad"), (10, "sbafad") },
            [PaymentType.NonXrpToXrp] = new[] { (1, "sxd"), (2, "saxd"), (6, "saaxd"), (7, "sbxd"), (8, "sabxd"), (9, "sabaxd") },
            [PaymentType.NonXrpToSame] = new[]
            {
                (1, "sad"), (1, "sfd"), (4, "safd"), (4, "sfad"), (5, "saad"), (5, "sbfd"), (6, "sxfad"), (6, "safad"),
                (6, "saxfd"), (6, "saxfad"), (6, "sabfd"), (7, "saaad"),
            },
            [PaymentType.NonXrpToNonXrp] = new[]
            {
                (1, "sfad"), (1, "safd"), (3, "safad"), (4, "sxfd"), (5, "saxfd"), (5, "sxfad"), (5, "sbfd"), (6, "saxfad"),
                (6, "sabfd"), (7, "saafd"), (8, "saafad"), (9, "safaad"),
            },
        };

        private static readonly Dictionary<string, byte[]> AccountIds = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        private readonly PathfindingSource _source;
        private readonly LineCache _cache;
        private readonly LedgerRules _rules;
        private readonly string _srcAccount;
        private readonly string _dstAccount;
        private readonly string _effectiveDst;
        private readonly XrplAmount _dstAmount;
        private readonly string _srcCurrency;
        private readonly string _srcIssuer;
        private readonly XrplAmount _srcAmount;
        private readonly bool _convertAll;
        private readonly string _domain;

        private readonly PfPathSet _completePaths = new PfPathSet();
        private readonly Dictionary<string, PfPathSet> _paths = new Dictionary<string, PfPathSet>(StringComparer.Ordinal);
        private readonly Dictionary<AssetId, int> _pathsOutCount = new Dictionary<AssetId, int>();
        private List<PathRank> _pathRanks = new List<PathRank>();
        private PfElement _sourceElement;
        private XrplAmount _remainingAmount;
        private DexWorld _world;

        internal sealed class PathRank
        {
            internal ulong Quality { get; init; }

            internal int Length { get; init; }

            internal XrplAmount Liquidity { get; init; }

            internal int Index { get; init; }
        }

        internal Pathfinder(
            PathfindingSource source,
            LineCache cache,
            LedgerRules rules,
            string srcAccount,
            string dstAccount,
            string srcCurrency,
            string srcIssuer,
            XrplAmount dstAmount,
            XrplAmount? srcAmount,
            string domain)
        {
            _source = source;
            _cache = cache;
            _rules = rules;
            _srcAccount = srcAccount;
            _dstAccount = dstAccount;
            _dstAmount = dstAmount;
            _effectiveDst = dstAmount.Kind == AmountKind.Xrp ? dstAccount : DexAssets.Issuer(dstAmount.Asset);
            _srcCurrency = PathCurrency.Normalize(srcCurrency);
            _srcIssuer = srcIssuer;
            _srcAmount = srcAmount ?? MinusOne(_srcCurrency, srcIssuer ?? (_srcCurrency == "XRP" ? string.Empty : srcAccount));
            _convertAll = ConvertAllCheck(dstAmount);
            _domain = string.IsNullOrEmpty(domain) ? null : domain;
        }

        /// <summary>The complete paths found, in the order they were found.</summary>
        internal IReadOnlyList<PfPath> CompletePaths => _completePaths.Paths;

        private string DstCurrency => PathCurrency.Of(_dstAmount.Asset);

        // ---- amounts ----

        /// <summary><c>STAmount(asset, 1, 0, true)</c>: minus one, "no limit" or "as much as possible".</summary>
        internal static XrplAmount MinusOne(string currency, string issuer)
        {
            IssuedCurrency asset = currency == "XRP" ? new IssuedCurrency { Currency = "XRP" }
                : PathCurrency.IsMpt(currency) ? DexAssets.Mpt(currency)
                : new IssuedCurrency { Currency = currency, Issuer = issuer };
            return XrplAmount.Parse(asset, "-1");
        }

        /// <summary><c>largestAmount</c>: all the XRP there is, the largest issued amount, or the most of an MPT.</summary>
        internal static XrplAmount LargestAmount(XrplAmount amount) =>
            amount.Kind switch
            {
                AmountKind.Xrp => XrplAmount.FromUnits(amount.Asset, AmountKind.Xrp, (long)XrplAmount.MaxDrops),
                AmountKind.Mpt => XrplAmount.FromUnits(amount.Asset, AmountKind.Mpt, (long)XrplAmount.MaxMptAmount),
                _ => XrplAmount.Canonical(amount.Asset, AmountKind.Iou, false, XrplAmount.MaxIouMantissa, XrplAmount.MaxIouExponent, Xrpl.BinaryCodec.Numbers.NumberRounding.ToNearest),
            };

        internal static XrplAmount ConvertAmount(XrplAmount amount, bool all) => all ? LargestAmount(amount) : amount;

        internal static bool ConvertAllCheck(XrplAmount amount) => amount == LargestAmount(amount);

        // ---- findPaths ----

        /// <summary><c>findPaths</c>: false when the request cannot be served at all.</summary>
        internal async Task<bool> FindPathsAsync(int searchLevel, CancellationToken cancellationToken)
        {
            if (_dstAmount.IsZero)
                return false;

            bool sameAsset = _srcCurrency == DstCurrency;
            if (_srcAccount == _dstAccount && _dstAccount == _effectiveDst && sameAsset)
                return false;
            if (_srcAccount == _effectiveDst && sameAsset)
                return true;

            bool currencyIsXrp = _srcCurrency == "XRP";
            bool useIssuerAccount = _srcIssuer != null && !currencyIsXrp && _srcIssuer.Length != 0;
            string account = useIssuerAccount ? _srcIssuer : _srcAccount;
            string issuer = currencyIsXrp ? string.Empty : account;
            _sourceElement = PfElement.Of(account, _srcCurrency, issuer);

            bool srcXrp = currencyIsXrp;
            bool dstXrp = _dstAmount.Kind == AmountKind.Xrp;

            if (await _source.AccountAsync(_srcAccount, cancellationToken).ConfigureAwait(false) == null)
                return false;
            if (_effectiveDst != _dstAccount && await _source.AccountAsync(_effectiveDst, cancellationToken).ConfigureAwait(false) == null)
                return false;
            if (await _source.AccountAsync(_dstAccount, cancellationToken).ConfigureAwait(false) == null)
            {
                // Only XRP can fund a new account, and at least its reserve.
                if (!dstXrp)
                    return false;
                if (_dstAmount.StMantissa < _source.ReserveBase && !_dstAmount.IsNegative)
                    return false;
            }

            PaymentType paymentType =
                srcXrp && dstXrp ? PaymentType.XrpToXrp :
                srcXrp ? PaymentType.XrpToNonXrp :
                dstXrp ? PaymentType.NonXrpToXrp :
                sameAsset ? PaymentType.NonXrpToSame :
                PaymentType.NonXrpToNonXrp;

            foreach ((int cost, string path) in PathTable[paymentType])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cost <= searchLevel)
                {
                    await AddPathsForTypeAsync(path, cancellationToken).ConfigureAwait(false);
                    if (_completePaths.Count > MaxCompletePaths)
                        break;
                }
            }

            return true;
        }

        private static NodeType NodeOf(char c) => c switch
        {
            's' => NodeType.Source,
            'a' => NodeType.Accounts,
            'b' => NodeType.Books,
            'x' => NodeType.XrpBook,
            'f' => NodeType.DestBook,
            _ => NodeType.Destination,
        };

        /// <summary><c>addPathsForType</c>: the paths of a shape, built on those of its shape minus the last node.</summary>
        private async Task<PfPathSet> AddPathsForTypeAsync(string pathType, CancellationToken cancellationToken)
        {
            if (_paths.TryGetValue(pathType, out PfPathSet existing))
                return existing;

            if (pathType.Length == 0)
            {
                PfPathSet empty = new PfPathSet();
                _paths[pathType] = empty;
                return empty;
            }

            PfPathSet parentPaths = await AddPathsForTypeAsync(pathType.Substring(0, pathType.Length - 1), cancellationToken).ConfigureAwait(false);
            PfPathSet pathsOut = new PfPathSet();
            _paths[pathType] = pathsOut;

            switch (NodeOf(pathType[pathType.Length - 1]))
            {
                case NodeType.Source:
                    pathsOut.Add(new PfPath());
                    break;
                case NodeType.Accounts:
                    await AddLinksAsync(parentPaths, pathsOut, AddAccounts, cancellationToken).ConfigureAwait(false);
                    break;
                case NodeType.Books:
                    await AddLinksAsync(parentPaths, pathsOut, AddBooks, cancellationToken).ConfigureAwait(false);
                    break;
                case NodeType.XrpBook:
                    await AddLinksAsync(parentPaths, pathsOut, AddBooks | ObXrp, cancellationToken).ConfigureAwait(false);
                    break;
                case NodeType.DestBook:
                    await AddLinksAsync(parentPaths, pathsOut, AddBooks | ObLast, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await AddLinksAsync(parentPaths, pathsOut, AddAccounts | AcLast, cancellationToken).ConfigureAwait(false);
                    break;
            }

            return pathsOut;
        }

        private async Task AddLinksAsync(PfPathSet currentPaths, PfPathSet incompletePaths, int addFlags, CancellationToken cancellationToken)
        {
            // Adding may grow the parent set when a shape repeats; walk what is there now.
            List<PfPath> snapshot = currentPaths.Paths.ToList();
            foreach (PfPath path in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AddLinkAsync(path, incompletePaths, addFlags, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary><c>issueMatchesOrigin</c>: the source's own currency or MPT, from the source or its issuer.</summary>
        private bool IssueMatchesOrigin(IssuedCurrency asset)
        {
            AssetId id = AssetId.Of(asset);
            bool matchingAsset = id.Currency == _srcCurrency;
            bool matchingAccount = id.IsXrp ||
                                   (_srcIssuer != null && id.Issuer == _srcIssuer) ||
                                   id.Issuer == _srcAccount;
            return matchingAsset && matchingAccount;
        }

        /// <summary>
        /// <c>getPathsOut</c>: how many ways lead on from <paramref name="account"/> in the
        /// currency - its books and the lines that can carry it - a line to the destination
        /// counting 10,000.
        /// </summary>
        private async Task<int> PathsOutAsync(string currency, string account, bool outgoing, bool isDstCurrency, string dstAccount, CancellationToken cancellationToken)
        {
            // An MPT's key is its issuance, whoever reaches it.
            bool mpt = PathCurrency.IsMpt(currency);
            AssetId asset = mpt ? AssetId.OfMpt(currency) : new AssetId(currency, account);
            if (_pathsOutCount.TryGetValue(asset, out int known))
                return known;

            _pathsOutCount[asset] = 0;
            DexAccount sle = await _source.AccountAsync(account, cancellationToken).ConfigureAwait(false);
            if (sle == null)
                return 0;

            if (mpt)
            {
                int mptCount = await MptPathsOutAsync(currency, account, isDstCurrency, dstAccount, cancellationToken).ConfigureAwait(false);
                _pathsOutCount[asset] = mptCount;
                return mptCount;
            }

            int count = 0;
            if (!sle.GlobalFreeze)
            {
                count = _source.Books.BooksFrom(asset.ToAsset(), _domain).Count;
                List<DexTrustLine> lines = await _cache.RippleLinesAsync(account, outgoing, cancellationToken).ConfigureAwait(false);
                foreach (DexTrustLine line in lines ?? new List<DexTrustLine>())
                {
                    if (PathCurrency.Normalize(line.Balance.Asset.Currency) != currency)
                        continue;
                    if (CannotCarry(line, sle.RequireAuth))
                        continue;
                    if (isDstCurrency && dstAccount == line.Balance.Asset.Issuer)
                    {
                        count += 10000;
                        continue;
                    }

                    if (line.PeerNoRipple || line.Frozen)
                        continue;

                    count++;
                }
            }

            _pathsOutCount[asset] = count;
            return count;
        }

        /// <summary>
        /// <c>getPathsOut</c> for an MPT: none while the issuance is locked; otherwise its books,
        /// and each of the account's MPTs of the issuance it can send and is not locked out of -
        /// one that reaches the destination's issuer counting 10,000.
        /// </summary>
        private async Task<int> MptPathsOutAsync(string mptKey, string account, bool isDstAsset, string dstAccount, CancellationToken cancellationToken)
        {
            DexMptIssuance issuance = await _source.IssuanceAsync(mptKey, cancellationToken).ConfigureAwait(false);
            bool authRequired = !await MptAuthorizedAsync(mptKey, issuance, account, cancellationToken).ConfigureAwait(false);
            if (issuance?.Locked == true)
                return 0;

            int count = _source.Books.BooksFrom(DexAssets.Mpt(mptKey), _domain).Count;
            foreach (PathfindMpt held in await _source.MptsAsync(account, cancellationToken).ConfigureAwait(false))
            {
                if (held.MptIssuanceId != mptKey || !held.CanSend(account) || authRequired)
                    continue;

                if (isDstAsset && dstAccount == DexAssets.MptIssuer(held.MptIssuanceId))
                {
                    count += 10000;
                    continue;
                }

                DexMptHolding token = await _source.MpTokenAsync(account, mptKey, cancellationToken).ConfigureAwait(false);
                if (token?.Locked == true)
                    continue;

                count++;
            }

            return count;
        }

        /// <summary>
        /// <c>requireAuth</c> of an MPT for the path finder: the issuer always; a holder with an
        /// MPToken the issuance does not require authorizing, or that is authorized.
        /// </summary>
        private async Task<bool> MptAuthorizedAsync(string mptKey, DexMptIssuance issuance, string account, CancellationToken cancellationToken)
        {
            if (issuance == null)
                return false;
            if (string.Equals(account, DexAssets.MptIssuer(mptKey), StringComparison.Ordinal))
                return true;

            DexMptHolding token = await _source.MpTokenAsync(account, mptKey, cancellationToken).ConfigureAwait(false);
            return token != null && (!issuance.RequireAuth || token.Authorized);
        }

        /// <summary>A line the account holds nothing on and has no credit left on - or, needing authorization, is not authorized on.</summary>
        private static bool CannotCarry(DexTrustLine line, bool authRequired)
        {
            XrplAmount balance = line.Balance;
            if (StepMath.IsPositive(balance))
                return false;

            XrplAmount? peerLimit = line.PeerLimit;
            return peerLimit == null || peerLimit.Value.IsZero ||
                   (-balance).WithAsset(balance.Asset) >= peerLimit.Value.WithAsset(balance.Asset) ||
                   (authRequired && !line.Authorized);
        }

        /// <summary><c>isNoRipple</c>: whether <paramref name="toAccount"/> set NoRipple on its line with <paramref name="fromAccount"/>.</summary>
        private async Task<bool> IsNoRippleAsync(string fromAccount, string toAccount, string currency, CancellationToken cancellationToken)
        {
            DexTrustLine line = await _source.LineAsync(toAccount, fromAccount, currency, cancellationToken).ConfigureAwait(false);
            return line != null && line.NoRipple;
        }

        /// <summary><c>isNoRippleOut</c>: whether the path ends on an account that set NoRipple toward the one before it.</summary>
        private async Task<bool> IsNoRippleOutAsync(PfPath currentPath, CancellationToken cancellationToken)
        {
            if (currentPath.IsEmpty)
                return false;

            PfElement end = currentPath.Back;
            if ((end.Type & PfElement.TypeAccount) == 0)
                return false;

            string fromAccount = currentPath.Count == 1 ? _srcAccount : currentPath[currentPath.Count - 2].Account;
            return end.HasCurrency && await IsNoRippleAsync(fromAccount, end.Account, end.Currency, cancellationToken).ConfigureAwait(false);
        }

        /// <summary><c>addLink</c>: the path extended by an account or an order book, or completed.</summary>
        private async Task AddLinkAsync(PfPath currentPath, PfPathSet incompletePaths, int addFlags, CancellationToken cancellationToken)
        {
            PfElement pathEnd = currentPath.IsEmpty ? _sourceElement : currentPath.Back;
            string endCurrency = pathEnd.Currency;
            string endIssuer = pathEnd.Issuer;
            string endAccount = pathEnd.Account;
            bool onXrp = endCurrency == "XRP";
            bool hasEffectiveDestination = _effectiveDst != _dstAccount;
            string dstCurrency = DstCurrency;

            if ((addFlags & AddAccounts) != 0)
            {
                if (onXrp)
                {
                    if (_dstAmount.Kind == AmountKind.Xrp && !currentPath.IsEmpty)
                        _completePaths.Add(currentPath);
                }
                else
                {
                    DexAccount sleEnd = await _source.AccountAsync(endAccount, cancellationToken).ConfigureAwait(false);
                    if (sleEnd != null)
                    {
                        bool requireAuth = sleEnd.RequireAuth;
                        bool isEndCurrency = endCurrency == dstCurrency;
                        bool isNoRippleOut = await IsNoRippleOutAsync(currentPath, cancellationToken).ConfigureAwait(false);
                        bool destOnly = (addFlags & AcLast) != 0;

                        List<(int Priority, string Account)> candidates = new List<(int, string)>();

                        // A currency leaves along the account's lines; an MPT goes back to its
                        // issuer, from the account's MPTs - it does not ripple.
                        List<(string Account, bool PeerOutgoing, bool Correct, bool Blocked)> links = new List<(string, bool, bool, bool)>();
                        if (PathCurrency.IsMpt(endCurrency))
                        {
                            foreach (PathfindMpt mpt in await _source.MptsAsync(endAccount, cancellationToken).ConfigureAwait(false))
                                links.Add((DexAssets.MptIssuer(mpt.MptIssuanceId), false, mpt.MptIssuanceId == endCurrency, !mpt.CanSend(endAccount)));
                        }
                        else
                        {
                            List<DexTrustLine> lines = await _cache.RippleLinesAsync(endAccount, !isNoRippleOut, cancellationToken).ConfigureAwait(false);
                            foreach (DexTrustLine line in lines ?? new List<DexTrustLine>())
                            {
                                links.Add((
                                    line.Balance.Asset.Issuer,
                                    !line.PeerNoRipple,
                                    PathCurrency.Normalize(line.Balance.Asset.Currency) == endCurrency,
                                    CannotCarry(line, requireAuth) || (isNoRippleOut && line.NoRipple)));
                            }
                        }

                        foreach ((string account, bool peerOutgoing, bool correctAsset, bool blocked) in links)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (hasEffectiveDestination && account == _dstAccount)
                                continue;

                            bool toDestination = account == _effectiveDst;
                            if (destOnly && !toDestination)
                                continue;

                            if (!correctAsset || currentPath.HasSeen(account, endCurrency, account))
                                continue;

                            if (blocked)
                                continue;

                            if (toDestination)
                            {
                                if (endCurrency == dstCurrency)
                                {
                                    if (!currentPath.IsEmpty)
                                        _completePaths.Add(currentPath);
                                }
                                else if (!destOnly)
                                {
                                    candidates.Add((HighPriority, account));
                                }
                            }
                            else if (account == _srcAccount)
                            {
                                // Going back to the source is no path.
                            }
                            else
                            {
                                int out_ = await PathsOutAsync(endCurrency, account, peerOutgoing, isEndCurrency, _effectiveDst, cancellationToken).ConfigureAwait(false);
                                if (out_ != 0)
                                    candidates.Add((out_, account));
                            }
                        }

                        if (candidates.Count > 0)
                        {
                            candidates.Sort((a, b) => CompareCandidates(a, b));
                            int count = candidates.Count;
                            if (count > 10 && endAccount != _srcAccount)
                                count = 10;
                            else if (count > 50)
                                count = 50;

                            for (int i = 0; i < count; i++)
                            {
                                string account = candidates[i].Account;
                                incompletePaths.AssembleAdd(currentPath, PfElement.Typed(PfElement.TypeAccount, account, endCurrency, account));
                            }
                        }
                    }
                }
            }

            if ((addFlags & AddBooks) != 0)
            {
                IssuedCurrency endAsset = new AssetId(endCurrency, onXrp ? string.Empty : endIssuer).ToAsset();
                if ((addFlags & ObXrp) != 0)
                {
                    if (!onXrp && _source.Books.HasBookToXrp(endAsset, _domain))
                        incompletePaths.AssembleAdd(currentPath, PfElement.Typed(PfElement.TypeCurrency, string.Empty, "XRP", string.Empty));
                }
                else
                {
                    bool destOnly = (addFlags & ObLast) != 0;
                    foreach (IssuedCurrency bookOut in _source.Books.BooksFrom(endAsset, _domain))
                    {
                        AssetId @out = AssetId.Of(bookOut);
                        if (currentPath.HasSeen(string.Empty, @out.Currency, @out.Issuer) ||
                            IssueMatchesOrigin(bookOut) ||
                            (destOnly && @out.Currency != dstCurrency))
                        {
                            continue;
                        }

                        PfPath newPath = new PfPath(currentPath);
                        if (@out.IsXrp)
                        {
                            newPath.Add(PfElement.Typed(PfElement.TypeCurrency, string.Empty, "XRP", string.Empty));
                            if (_dstAmount.Kind == AmountKind.Xrp)
                                _completePaths.Add(newPath);
                            else
                                incompletePaths.Add(newPath);
                        }
                        else if (!currentPath.HasSeen(@out.Issuer, @out.Currency, @out.Issuer))
                        {
                            int assetType = @out.IsMpt ? PfElement.TypeMpt : PfElement.TypeCurrency;
                            PfElement book = PfElement.Typed(assetType | PfElement.TypeIssuer, string.Empty, @out.Currency, @out.Issuer);

                            // book -> account -> book: the account is implied, the book replaces it.
                            if (newPath.Count >= 2 && newPath.Back.IsAccount && newPath[newPath.Count - 2].IsOffer)
                                newPath[newPath.Count - 1] = book;
                            else
                                newPath.Add(book);

                            if (hasEffectiveDestination && @out.Issuer == _dstAccount && @out.Currency == dstCurrency)
                            {
                                // A required issuer was skipped.
                            }
                            else if (@out.Issuer == _effectiveDst && @out.Currency == dstCurrency)
                            {
                                _completePaths.Add(newPath);
                            }
                            else
                            {
                                incompletePaths.AssembleAdd(newPath, PfElement.Typed(PfElement.TypeAccount, @out.Issuer, @out.Currency, @out.Issuer));
                            }
                        }
                    }
                }
            }
        }

        /// <summary><c>compareAccountCandidate</c>: more ways out first, then the larger account id.</summary>
        private static int CompareCandidates((int Priority, string Account) first, (int Priority, string Account) second)
        {
            if (first.Priority != second.Priority)
                return second.Priority.CompareTo(first.Priority);

            return CompareAccounts(second.Account, first.Account);
        }

        /// <summary>Account ids compared as the 160-bit numbers they are.</summary>
        internal static int CompareAccounts(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal))
                return 0;

            byte[] x = AccountId(a);
            byte[] y = AccountId(b);
            for (int i = 0; i < x.Length && i < y.Length; i++)
            {
                if (x[i] != y[i])
                    return x[i].CompareTo(y[i]);
            }

            return x.Length.CompareTo(y.Length);
        }

        private static byte[] AccountId(string address)
        {
            if (string.IsNullOrEmpty(address))
                return new byte[20];

            lock (AccountIds)
            {
                if (!AccountIds.TryGetValue(address, out byte[] bytes))
                    AccountIds[address] = bytes = XrplCodec.DecodeAccountID(address);
                return bytes;
            }
        }

        // ---- ranking ----

        /// <summary>Reads the state the engine needs to rank the paths found, for these send-max assets.</summary>
        internal async Task LoadStateAsync(IEnumerable<IssuedCurrency> sendMaxAssets, IEnumerable<PfPath> extraPaths, CancellationToken cancellationToken)
        {
            List<StrandBuilder.Request> requests = new List<StrandBuilder.Request>();
            foreach (IssuedCurrency asset in sendMaxAssets)
            {
                requests.Add(new StrandBuilder.Request
                {
                    Source = _srcAccount,
                    Destination = _dstAccount,
                    Deliver = _dstAmount.Asset,
                    SendMax = asset,
                    DomainId = _domain,
                });
            }

            List<IReadOnlyList<PathElement>> paths = _completePaths.Paths.Concat(extraPaths).Select(p => p.ToPathElements()).ToList();
            DexSnapshot state = await _source.StateAsync(requests, paths, cancellationToken).ConfigureAwait(false);
            _world = new DexWorld(state);
        }

        /// <summary>A fresh view of the ledger, as a new <c>PaymentSandbox</c> over it.</summary>
        internal DexView FreshView() => new DexView(_world, _rules);

        /// <summary><c>computePathRanks</c>: what the default path leaves to deliver, then each path ranked.</summary>
        internal void ComputePathRanks(int maxPaths)
        {
            _remainingAmount = ConvertAmount(_dstAmount, _convertAll);
            RippleCalc.Output rc = RippleCalc.Calculate(
                FreshView(), _srcAmount, _remainingAmount, _dstAccount, _srcAccount,
                Array.Empty<IReadOnlyList<PathElement>>(), _domain, defaultPaths: true, partialPayment: true);
            if (rc.Succeeded)
                _remainingAmount = XrplAmountMath.Subtract(_remainingAmount, rc.ActualOut.WithAsset(_remainingAmount.Asset), _rules);

            _pathRanks = RankPaths(maxPaths, _completePaths.Paths);
        }

        /// <summary><c>getPathLiquidity</c>: the path's quality at the minimum useful amount, and all it can carry.</summary>
        private (string Result, XrplAmount AmountOut, ulong Quality) PathLiquidity(PfPath path, XrplAmount minDstAmount)
        {
            IReadOnlyList<IReadOnlyList<PathElement>> pathSet = new[] { path.ToPathElements() };
            DexView sandbox = FreshView();

            RippleCalc.Output rc = RippleCalc.Calculate(
                sandbox, _srcAmount, minDstAmount, _dstAccount, _srcAccount, pathSet, _domain, defaultPaths: false, partialPayment: _convertAll);
            if (!rc.Succeeded)
                return (rc.Result, default, 0);

            ulong quality = rc.ActualOut.IsZero ? 0 : XrplQuality.FromAmounts(rc.ActualIn, rc.ActualOut, _rules).Value;
            XrplAmount amountOut = rc.ActualOut.WithAsset(_dstAmount.Asset);

            if (!_convertAll)
            {
                // What else the path carries, on top of the minimum already taken.
                XrplAmount rest = XrplAmountMath.Subtract(_dstAmount, amountOut, _rules);
                rc = RippleCalc.Calculate(
                    sandbox, _srcAmount, rest, _dstAccount, _srcAccount, pathSet, _domain, defaultPaths: false, partialPayment: true);
                if (rc.Succeeded)
                    amountOut = XrplAmountMath.Add(amountOut, rc.ActualOut.WithAsset(_dstAmount.Asset), _rules);
            }

            return ("tesSUCCESS", amountOut, quality);
        }

        /// <summary><c>rankPaths</c>: each useful path ranked by quality, liquidity and length.</summary>
        private List<PathRank> RankPaths(int maxPaths, IReadOnlyList<PfPath> paths)
        {
            List<PathRank> ranked = new List<PathRank>();
            XrplAmount minDstAmount = _convertAll
                ? LargestAmount(_dstAmount)
                : XrplAmountMath.Divide(_dstAmount, XrplAmount.FromUnits(new IssuedCurrency { Currency = "XRP" }, AmountKind.Xrp, maxPaths + 2), _dstAmount.Asset, _rules);

            for (int i = 0; i < paths.Count; i++)
            {
                PfPath path = paths[i];
                if (path.IsEmpty)
                    continue;

                (string result, XrplAmount liquidity, ulong quality) = PathLiquidity(path, minDstAmount);
                if (result == "tesSUCCESS")
                    ranked.Add(new PathRank { Quality = quality, Length = path.Count, Liquidity = liquidity, Index = i });
            }

            ranked.Sort((a, b) =>
            {
                if (!_convertAll && a.Quality != b.Quality)
                    return a.Quality.CompareTo(b.Quality);
                if (a.Liquidity != b.Liquidity)
                    return a.Liquidity > b.Liquidity ? -1 : 1;
                if (a.Length != b.Length)
                    return a.Length.CompareTo(b.Length);

                return b.Index.CompareTo(a.Index);
            });
            return ranked;
        }

        /// <summary>
        /// <c>getBestPaths</c>: the best-ranked paths, up to <paramref name="maxPaths"/>, the last
        /// one able to fill what the others leave; and, when none of those fills the whole amount
        /// alone, the best single path that does.
        /// </summary>
        internal (List<PfPath> Best, PfPath FullLiquidityPath) BestPaths(int maxPaths, string srcIssuer)
        {
            List<PfPath> best = new List<PfPath>();
            PfPath fullLiquidityPath = null;
            if (_completePaths.Count == 0)
                return (best, null);

            bool issuerIsSender = _srcCurrency == "XRP" || srcIssuer == _srcAccount;
            XrplAmount remaining = _remainingAmount;

            foreach (PathRank rank in _pathRanks)
            {
                PfPath path = _completePaths[rank.Index];
                int pathsLeft = maxPaths - best.Count;
                if (pathsLeft <= 0 && fullLiquidityPath != null)
                    break;

                bool startsWithIssuer = false;
                if (!issuerIsSender)
                {
                    // The path has to start at the source's issuer, which is then implied.
                    if (path.Count == 1 || path.Front.Account != srcIssuer)
                        continue;

                    startsWithIssuer = true;
                }

                PfPath chosen = startsWithIssuer ? path.WithoutFirst() : path;
                if (pathsLeft > 1 || (pathsLeft > 0 && rank.Liquidity >= remaining))
                {
                    remaining = XrplAmountMath.Subtract(remaining, rank.Liquidity.WithAsset(remaining.Asset), _rules);
                    best.Add(chosen);
                }
                else if (pathsLeft == 0 && rank.Liquidity >= _dstAmount && fullLiquidityPath == null)
                {
                    fullLiquidityPath = chosen;
                }
            }

            return (best, fullLiquidityPath);
        }
    }
}
