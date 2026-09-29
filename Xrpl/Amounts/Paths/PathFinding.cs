using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Models.Common;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>What to find paths for, as <c>ripple_path_find</c> takes it.</summary>
    public sealed class PathFindRequest
    {
        /// <summary>The paying account.</summary>
        public string SourceAccount { get; init; }

        /// <summary>The receiving account.</summary>
        public string DestinationAccount { get; init; }

        /// <summary>
        /// What to deliver. Minus one of an asset (<c>"-1"</c>) asks for as much as possible,
        /// which <see cref="SendMax"/> then limits.
        /// </summary>
        public XrplAmount DestinationAmount { get; init; }

        /// <summary>The most to spend; only with a destination amount of minus one.</summary>
        public XrplAmount? SendMax { get; init; }

        /// <summary>
        /// The assets the source may pay in, at most 18; an issued currency without an issuer
        /// means any issuer the source holds. When empty, every asset the source can send is tried.
        /// </summary>
        public IReadOnlyList<IssuedCurrency> SourceCurrencies { get; init; } = Array.Empty<IssuedCurrency>();

        /// <summary>The permissioned domain whose books to use; null for the open books.</summary>
        public string DomainId { get; init; }

        /// <summary>How deep to search: 2 is the node's default, 7 its slow search, 10 the deepest.</summary>
        public int SearchLevel { get; init; } = 2;
    }

    /// <summary>What a path search found, as <c>ripple_path_find</c> returns it.</summary>
    public sealed class PathFindResult
    {
        internal PathFindResult(string error, IReadOnlyList<PathFindAlternative> alternatives, IReadOnlyList<string> destinationCurrencies, bool destinationTag)
        {
            Error = error;
            Alternatives = alternatives;
            DestinationCurrencies = destinationCurrencies;
            DestinationTagRequired = destinationTag;
        }

        /// <summary>The node's error code for a request it would refuse, such as <c>srcActNotFound</c> or <c>dstAmtMalformed</c>; null otherwise.</summary>
        public string Error { get; }

        /// <summary>One way to pay for each source asset that can: what it costs, and the paths.</summary>
        public IReadOnlyList<PathFindAlternative> Alternatives { get; }

        /// <summary>The currencies the destination can receive.</summary>
        public IReadOnlyList<string> DestinationCurrencies { get; }

        /// <summary>Whether the destination requires a destination tag.</summary>
        public bool DestinationTagRequired { get; }
    }

    /// <summary>One way to make the payment: from one source asset, along up to four paths and the default one.</summary>
    public sealed class PathFindAlternative
    {
        internal PathFindAlternative(XrplAmount sourceAmount, List<List<PathStep>> paths, XrplAmount? destinationAmount)
        {
            SourceAmount = sourceAmount;
            PathsComputed = paths;
            DestinationAmount = destinationAmount;
        }

        /// <summary>What the source spends; an issued currency's issuer is the source for any issuer it holds.</summary>
        public XrplAmount SourceAmount { get; }

        /// <summary>The paths, for the payment's <c>Paths</c>.</summary>
        public List<List<PathStep>> PathsComputed { get; }

        /// <summary>What reaches the destination, when as much as possible was asked for; null otherwise.</summary>
        public XrplAmount? DestinationAmount { get; }
    }

    /// <summary>
    /// Payment paths found locally, as rippled 3.4.0's <c>ripple_path_find</c> finds them: its
    /// <c>PathRequest</c> and <c>Pathfinder</c> over a <see cref="PathfindingSource"/>, each
    /// path ranked with the payment engine, and the cost of the best ones computed.
    /// </summary>
    /// <remarks>
    /// Against the same ledger and the same order books, the result matches the node's but for
    /// the order of the alternatives and of equally good books, which the node keeps in hash
    /// sets. The node's index keeps books whose last offer is gone until it rebuilds; an index
    /// read from the ledger does not, which can change which accounts are tried first when an
    /// account has more than ten ways on.
    /// </remarks>
    public static class PathFinding
    {
        /// <summary>The paths an alternative carries, the default one aside (<c>PathRequest::kMaxPaths</c>).</summary>
        public const int MaxPaths = 4;

        private const int MaxSourceCurrencies = 18;
        private const int MaxAutoSourceCurrencies = 88;

        /// <summary>Finds the paths of <paramref name="request"/> in <paramref name="source"/>.</summary>
        /// <param name="source">The ledger to search.</param>
        /// <param name="request">What to pay.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <param name="cancellationToken">Cancels the search and the reads under it.</param>
        public static async Task<PathFindResult> FindAsync(
            PathfindingSource source,
            PathFindRequest request,
            LedgerRules rules = null,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (request?.SourceAccount == null || request.DestinationAccount == null || request.DestinationAmount.Asset == null)
                throw new ArgumentException("A path request needs a source, a destination and an amount.", nameof(request));

            // ripple_path_find runs outside any transaction, and its engine sees the rules that way.
            rules = (rules ?? new LedgerRules()).OutsideOfTransaction();
            string src = request.SourceAccount;
            string dst = request.DestinationAccount;
            XrplAmount dstAmount = request.DestinationAmount;
            IssuedCurrency dstAsset = dstAmount.Asset;
            string dstCurrency = PathCurrency.Normalize(dstAmount.Kind == AmountKind.Xrp ? "XRP" : dstAsset.Currency);
            bool convertAll = dstAmount == Pathfinder.MinusOne(dstCurrency, dstAmount.Kind == AmountKind.Xrp ? string.Empty : dstAsset.Issuer);

            // parseJson
            if (!convertAll && StepMath.IsNotPositive(dstAmount))
                return Error("dstAmtMalformed");

            XrplAmount? sendMax = request.SendMax;
            if (sendMax is { } max)
            {
                if (!convertAll)
                    return Error("dstAmtMalformed");

                string maxCurrency = PathCurrency.Normalize(max.Kind == AmountKind.Xrp ? "XRP" : max.Asset.Currency);
                XrplAmount minusOne = Pathfinder.MinusOne(maxCurrency, max.Kind == AmountKind.Xrp ? string.Empty : max.Asset.Issuer);
                if (StepMath.IsNotPositive(max) && max != minusOne)
                    return Error("sendMaxMalformed");
            }

            (string error, List<AssetId> sourceAssets) = SourceAssets(request, src, sendMax);
            if (error != null)
                return Error(error);

            // isValid
            if (await source.AccountAsync(src, cancellationToken).ConfigureAwait(false) == null)
                return Error("srcActNotFound");

            LineCache cache = new LineCache(source);
            DexAccount destination = await source.AccountAsync(dst, cancellationToken).ConfigureAwait(false);
            List<string> destinationCurrencies = new List<string> { "XRP" };
            if (destination == null)
            {
                if (dstAmount.Kind != AmountKind.Xrp)
                    return Error("actNotFound");
                if (!convertAll && dstAmount.StMantissa < source.ReserveBase)
                    return Error("dstAmtMalformed");
            }
            else
            {
                foreach (DexTrustLine line in await cache.RippleLinesAsync(dst, outgoing: true, cancellationToken).ConfigureAwait(false) ?? new List<DexTrustLine>())
                {
                    string currency = PathCurrency.Normalize(line.Balance.Asset.Currency);
                    XrplAmount limit = line.Limit?.WithAsset(line.Balance.Asset) ?? XrplAmount.Zero(line.Balance.Asset);
                    if (line.Balance < limit && !destinationCurrencies.Contains(currency))
                        destinationCurrencies.Add(currency);
                }
            }

            // findPaths: the source's own assets when none are given.
            if (sourceAssets.Count == 0)
            {
                bool sameAccount = src == dst;
                foreach (string currency in await SourceCurrenciesAsync(cache, src, cancellationToken).ConfigureAwait(false))
                {
                    if (sameAccount && currency == dstCurrency)
                        continue;
                    if (sourceAssets.Count >= MaxAutoSourceCurrencies)
                        return Error("internal");

                    AssetId asset = currency == "XRP" ? AssetId.Xrp : new AssetId(currency, src);
                    if (!sourceAssets.Contains(asset))
                        sourceAssets.Add(asset);
                }
            }

            XrplAmount target = Pathfinder.ConvertAmount(dstAmount, convertAll);
            Dictionary<string, Pathfinder> pathfinders = new Dictionary<string, Pathfinder>(StringComparer.Ordinal);
            List<PathFindAlternative> alternatives = new List<PathFindAlternative>();
            foreach (AssetId asset in sourceAssets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string sourceAccount = asset.IsXrp ? string.Empty : (asset.Issuer.Length != 0 ? asset.Issuer : src);
                XrplAmount maxAmount = sendMax ?? Pathfinder.MinusOne(asset.Currency, sourceAccount);

                if (!pathfinders.TryGetValue(asset.Currency, out Pathfinder pathfinder))
                {
                    pathfinder = new Pathfinder(source, cache, rules, src, dst, asset.Currency, null, target, sendMax, request.DomainId);
                    if (await pathfinder.FindPathsAsync(request.SearchLevel, cancellationToken).ConfigureAwait(false))
                    {
                        // The state covers the ranking, and the cost from each source asset of this currency.
                        List<IssuedCurrency> sendMaxAssets = new List<IssuedCurrency>
                        {
                            (sendMax ?? Pathfinder.MinusOne(asset.Currency, asset.IsXrp ? string.Empty : src)).Asset,
                            null,
                        };
                        foreach (AssetId sibling in sourceAssets.Where(a => a.Currency == asset.Currency))
                        {
                            string siblingAccount = sibling.IsXrp ? string.Empty : (sibling.Issuer.Length != 0 ? sibling.Issuer : src);
                            sendMaxAssets.Add((sendMax ?? Pathfinder.MinusOne(sibling.Currency, siblingAccount)).Asset);
                        }

                        await pathfinder.LoadStateAsync(sendMaxAssets, Array.Empty<PfPath>(), cancellationToken).ConfigureAwait(false);
                        pathfinder.ComputePathRanks(MaxPaths);
                    }
                    else
                    {
                        pathfinder = null;
                    }

                    pathfinders[asset.Currency] = pathfinder;
                }

                if (pathfinder == null)
                    continue;

                (List<PfPath> best, PfPath fullLiquidityPath) = pathfinder.BestPaths(MaxPaths, asset.IsXrp ? string.Empty : asset.Issuer);
                List<IReadOnlyList<PathElement>> paths = best.Select(p => p.ToPathElements()).ToList();

                RippleCalc.Output rc = RippleCalc.Calculate(
                    pathfinder.FreshView(), maxAmount, target, dst, src, paths, request.DomainId, defaultPaths: true, partialPayment: convertAll);

                if (!convertAll && fullLiquidityPath != null && (rc.Result == "terNO_LINE" || rc.Result == "tecPATH_PARTIAL"))
                {
                    best.Add(fullLiquidityPath);
                    paths.Add(fullLiquidityPath.ToPathElements());
                    rc = RippleCalc.Calculate(
                        pathfinder.FreshView(), maxAmount, target, dst, src, paths, request.DomainId, defaultPaths: true, partialPayment: false);
                }

                if (!rc.Succeeded)
                    continue;

                XrplAmount sourceAmount = rc.ActualIn.Kind == AmountKind.Iou
                    ? rc.ActualIn.WithAsset(new IssuedCurrency { Currency = rc.ActualIn.Asset.Currency, Issuer = sourceAccount })
                    : rc.ActualIn;
                alternatives.Add(new PathFindAlternative(
                    sourceAmount,
                    best.Select(p => p.ToPathSteps()).ToList(),
                    convertAll ? rc.ActualOut.WithAsset(dstAsset) : null));
            }

            return new PathFindResult(null, alternatives, destinationCurrencies, destination?.RequireDestinationTag == true);
        }

        private static PathFindResult Error(string error) =>
            new PathFindResult(error, Array.Empty<PathFindAlternative>(), Array.Empty<string>(), false);

        /// <summary>The source assets <c>parseJson</c> reads from <c>source_currencies</c>, reconciled with SendMax.</summary>
        private static (string Error, List<AssetId> Assets) SourceAssets(PathFindRequest request, string src, XrplAmount? sendMax)
        {
            List<AssetId> assets = new List<AssetId>();
            IReadOnlyList<IssuedCurrency> given = request.SourceCurrencies ?? Array.Empty<IssuedCurrency>();
            if (given.Count > MaxSourceCurrencies)
                return ("srcCurMalformed", assets);

            foreach (IssuedCurrency entry in given)
            {
                if (entry?.Currency == null)
                    return ("srcCurMalformed", assets);

                string currency = PathCurrency.Normalize(entry.Currency);
                string issuer = entry.Issuer;
                if (currency == "XRP")
                {
                    if (!string.IsNullOrEmpty(issuer))
                        return ("srcCurMalformed", assets);
                    issuer = string.Empty;
                }
                else if (string.IsNullOrEmpty(issuer))
                {
                    issuer = src;
                }

                AssetId asset;
                if (sendMax is { } max)
                {
                    // Only the currency of SendMax counts; its issuer and this one are reconciled.
                    AssetId maxAsset = AssetId.Of(max.Asset);
                    if (maxAsset.Currency != currency)
                        continue;

                    if (currency == "XRP")
                    {
                        asset = AssetId.Xrp;
                    }
                    else
                    {
                        if (issuer != src && maxAsset.Issuer != src && issuer != maxAsset.Issuer)
                            return ("srcIsrMalformed", assets);

                        asset = issuer != src ? new AssetId(currency, issuer)
                            : maxAsset.Issuer != src ? new AssetId(currency, maxAsset.Issuer)
                            : new AssetId(currency, src);
                    }
                }
                else
                {
                    asset = new AssetId(currency, issuer);
                }

                if (!assets.Contains(asset))
                    assets.Add(asset);
            }

            if (assets.Count == 0 && sendMax is { } sendMaxAsset)
                assets.Add(AssetId.Of(sendMaxAsset.Asset));

            return (null, assets);
        }

        /// <summary><c>accountSourceAssets</c>: XRP, and each currency the source holds or has credit left in.</summary>
        private static async Task<List<string>> SourceCurrenciesAsync(LineCache cache, string account, CancellationToken cancellationToken)
        {
            List<string> currencies = new List<string> { "XRP" };
            foreach (DexTrustLine line in await cache.RippleLinesAsync(account, outgoing: true, cancellationToken).ConfigureAwait(false) ?? new List<DexTrustLine>())
            {
                XrplAmount balance = line.Balance;
                bool canSend = StepMath.IsPositive(balance) ||
                               (line.PeerLimit is { } peerLimit && !peerLimit.IsZero && (-balance).WithAsset(balance.Asset) < peerLimit.WithAsset(balance.Asset));
                string currency = PathCurrency.Normalize(balance.Asset.Currency);
                if (canSend && !currencies.Contains(currency))
                    currencies.Add(currency);
            }

            return currencies;
        }
    }
}
