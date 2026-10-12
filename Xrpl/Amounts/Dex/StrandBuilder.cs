using System;
using System.Collections.Generic;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// One element of a payment path (<c>STPathElement</c>): an account, or an offer book named by
    /// its output currency or MPT, its issuer, or both. XRP is the currency <c>"XRP"</c>, and its
    /// issuer or account is the empty string.
    /// </summary>
    internal readonly struct PathElement
    {
        internal PathElement(string account, string currency, string issuer, string mpt = null)
        {
            Account = account;
            Currency = currency;
            Issuer = issuer;
            Mpt = mpt == null ? null : DexAssets.MptKey(mpt);
        }

        internal string Account { get; }

        internal string Currency { get; }

        internal string Issuer { get; }

        /// <summary>The MPT issuance the element names (<c>TypeMpt</c>); null for none.</summary>
        internal string Mpt { get; }

        internal bool IsAccount => Account != null;

        internal bool IsOffer => Account == null;

        internal bool HasCurrency => Currency != null;

        internal bool HasIssuer => Issuer != null;

        internal bool HasMpt => Mpt != null;

        /// <summary><c>TypeAsset</c>: a currency or an MPT.</summary>
        internal bool HasAsset => HasCurrency || HasMpt;

        internal bool IsEmpty => Account == null && Currency == null && Issuer == null && Mpt == null;

        internal static PathElement OfAccount(string account) => new PathElement(account, null, null);

        /// <summary>An offer element for <paramref name="asset"/>, its issuer named.</summary>
        internal static PathElement OfAsset(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) switch
            {
                AmountKind.Xrp => new PathElement(null, "XRP", string.Empty),
                AmountKind.Mpt => new PathElement(null, null, DexAssets.MptIssuer(asset.MptIssuanceId), asset.MptIssuanceId),
                _ => new PathElement(null, asset.Currency, asset.Issuer),
            };

        internal static bool IsXrpCurrency(string currency) => string.Equals(currency, "XRP", StringComparison.Ordinal);
    }

    /// <summary>
    /// rippled's <c>toStrand</c> and <c>toStrands</c>: a path, with the source, the destination
    /// and the implied issuers around it, turned into the steps the engine runs - a direct step
    /// for each trust line, a book step for each offer book, an XRP step at each XRP end - each
    /// step checked as it is built.
    /// </summary>
    internal static class StrandBuilder
    {
        /// <summary>The request the strands serve.</summary>
        internal sealed class Request
        {
            internal string Source { get; init; }

            internal string Destination { get; init; }

            internal IssuedCurrency Deliver { get; init; }

            internal IssuedCurrency SendMax { get; init; }

            internal XrplQuality? LimitQuality { get; init; }

            internal bool OfferCrossing { get; init; }

            internal AmmFlowContext AmmContext { get; init; }

            /// <summary>The permissioned domain whose books the strands walk; null for the open books.</summary>
            internal string DomainId { get; init; }
        }

        /// <summary><c>toStrands</c>: the default path, when allowed, and each given path, duplicates dropped.</summary>
        internal static (string Result, List<List<FlowStep>> Strands) ToStrands(
            DexView view,
            Request request,
            IReadOnlyList<IReadOnlyList<PathElement>> paths,
            bool addDefaultPath)
        {
            List<List<FlowStep>> result = new List<List<FlowStep>>();
            void Insert(List<FlowStep> strand)
            {
                foreach (List<FlowStep> existing in result)
                {
                    if (SameStrand(existing, strand))
                        return;
                }

                for (int position = 0; position < strand.Count; position++)
                    strand[position].Position = position;
                result.Add(strand);
            }

            if (addDefaultPath)
            {
                (string ter, List<FlowStep> strand) = ToStrand(view, request, Array.Empty<PathElement>());
                if (ter != null)
                {
                    if (IsMalformed(ter) || paths.Count == 0)
                        return (ter, new List<List<FlowStep>>());
                }
                else
                {
                    Insert(strand);
                }
            }
            else if (paths.Count == 0)
            {
                return ("temRIPPLE_EMPTY", new List<List<FlowStep>>());
            }

            string lastFailure = null;
            foreach (IReadOnlyList<PathElement> path in paths)
            {
                (string ter, List<FlowStep> strand) = ToStrand(view, request, path);
                if (ter != null)
                {
                    lastFailure = ter;
                    if (IsMalformed(ter))
                        return (ter, new List<List<FlowStep>>());
                }
                else
                {
                    Insert(strand);
                }
            }

            if (result.Count == 0)
                return (lastFailure, result);

            return (null, result);
        }

        private static bool IsMalformed(string ter) => ter.StartsWith("tem", StringComparison.Ordinal);

        private static bool IsXrpAccount(PathElement element) => element.IsAccount && element.Account.Length == 0;

        /// <summary>One step of a planned strand, before the ledger is consulted.</summary>
        internal enum HopKind
        {
            Direct,
            Book,
            XrpEndpoint,
            MptEndpoint,
        }

        /// <summary>A step as <c>toStrand</c> lays it out: the accounts and assets, and whether it ends the strand.</summary>
        internal sealed class Hop
        {
            internal HopKind Kind { get; init; }

            /// <summary>A direct or MPT step's source, or the account of an XRP step.</summary>
            internal string Source { get; init; }

            internal string Destination { get; init; }

            internal string Currency { get; init; }

            /// <summary>The issuance an MPT step moves.</summary>
            internal string Mpt { get; init; }

            internal IssuedCurrency In { get; init; }

            internal IssuedCurrency Out { get; init; }

            internal bool IsLast { get; init; }
        }

        /// <summary>
        /// <c>curAsset</c> while <c>toStrand</c> walks the path: a currency and its issuer, XRP
        /// with an empty issuer, or an MPT, whose issuer is fixed by its issuance.
        /// </summary>
        private struct PathAsset
        {
            internal string Currency;
            internal string Issuer;
            internal string Mpt;

            internal bool IsMpt => Mpt != null;

            internal bool IsXrp => !IsMpt && PathElement.IsXrpCurrency(Currency);

            internal static PathAsset Of(IssuedCurrency asset, string source) =>
                XrplAmount.KindOf(asset) switch
                {
                    AmountKind.Mpt => new PathAsset { Mpt = DexAssets.MptKey(asset.MptIssuanceId), Issuer = DexAssets.MptIssuer(asset.MptIssuanceId) },
                    AmountKind.Xrp => new PathAsset { Currency = "XRP", Issuer = string.Empty },
                    _ => new PathAsset { Currency = asset.Currency, Issuer = source },
                };

            internal IssuedCurrency ToAsset() =>
                IsMpt ? DexAssets.Mpt(Mpt)
                : IsXrp ? new IssuedCurrency { Currency = "XRP" }
                : new IssuedCurrency { Currency = Currency, Issuer = Issuer };

            /// <summary><c>PathAsset ==</c>: the same currency, or the same MPT, whoever issues the currency.</summary>
            internal bool SameAsset(PathElement element) =>
                IsMpt ? element.HasMpt && element.Mpt == Mpt : element.HasCurrency && string.Equals(element.Currency, Currency, StringComparison.Ordinal);
        }

        /// <summary>
        /// The layout of <c>toStrand</c>: the path normalized with the source, SendMax's issuer,
        /// the delivered asset's book and issuer, and the destination, then one hop per step. A
        /// malformed path stops the plan with its failure after the hops before it.
        /// </summary>
        internal static (List<Hop> Hops, string Failure, string StrandSource, string StrandDestination) Plan(Request request, IReadOnlyList<PathElement> path)
        {
            List<Hop> hops = new List<Hop>();
            string source = request.Source;
            string destination = request.Destination;
            IssuedCurrency deliver = request.Deliver;
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination))
                return (hops, "temBAD_PATH", source, destination);

            for (int index = 0; index < path.Count; index++)
            {
                PathElement element = path[index];
                bool bad = element.IsEmpty ||
                           (element.IsAccount && (element.HasIssuer || element.HasCurrency)) ||
                           (element.HasIssuer && element.Issuer.Length == 0) ||
                           (element.IsAccount && element.Account.Length == 0) ||
                           (element.HasCurrency && element.HasIssuer && PathElement.IsXrpCurrency(element.Currency) != (element.Issuer.Length == 0)) ||
                           (element.HasMpt && (element.HasCurrency || element.IsAccount)) ||
                           (element.HasMpt && element.HasIssuer && !string.Equals(element.Issuer, DexAssets.MptIssuer(element.Mpt), StringComparison.Ordinal)) ||
                           // An MPT does not ripple: nothing but another asset follows it.
                           (index > 0 && path[index - 1].HasMpt && (element.IsAccount || (element.HasIssuer && !element.HasAsset)));
                if (bad)
                    return (hops, "temBAD_PATH", source, destination);
            }

            IssuedCurrency start = request.SendMax ?? deliver;
            PathAsset current = PathAsset.Of(start, source);

            // The source first, as the transaction's Account, in the SendMax or the delivered
            // asset; an MPT's issuer is its own, never the source.
            List<PathElement> normalized = new List<PathElement>
            {
                current.IsMpt ? new PathElement(source, null, current.Issuer, current.Mpt) : new PathElement(source, current.Currency, current.Issuer),
            };

            // SendMax's issuer, when it is not the source, is the implied second hop - unless the
            // path starts at it.
            string sendMaxIssuer = request.SendMax == null ? null : DexAssets.Issuer(request.SendMax);
            if (sendMaxIssuer != null && !string.Equals(sendMaxIssuer, source, StringComparison.Ordinal) &&
                (path.Count == 0 || !path[0].IsAccount || !string.Equals(path[0].Account, sendMaxIssuer, StringComparison.Ordinal)))
            {
                normalized.Add(PathElement.OfAccount(sendMaxIssuer));
            }

            normalized.AddRange(path);

            // An offer book to the delivered asset, unless the path ends in it already. Offer
            // crossing takes a book even when only the issuer differs.
            PathElement lastAsset = default;
            for (int i = normalized.Count - 1; i >= 0; i--)
            {
                if (normalized[i].HasAsset)
                {
                    lastAsset = normalized[i];
                    break;
                }
            }

            PathAsset deliverAsset = PathAsset.Of(deliver, null);
            string deliverIssuer = DexAssets.Issuer(deliver);
            if (!deliverAsset.SameAsset(lastAsset) ||
                (request.OfferCrossing && !string.Equals(lastAsset.Issuer, deliverIssuer, StringComparison.Ordinal)))
            {
                normalized.Add(PathElement.OfAsset(deliver));
            }

            // The delivered asset's issuer, when it is not the destination, is the implied
            // second-to-last hop; after an offer that sells an MPT, that is the MPT's issuer.
            PathElement back = normalized[normalized.Count - 1];
            if (!((back.IsAccount && string.Equals(back.Account, deliverIssuer, StringComparison.Ordinal)) ||
                  string.Equals(destination, deliverIssuer, StringComparison.Ordinal)))
            {
                normalized.Add(PathElement.OfAccount(deliverIssuer));
            }

            back = normalized[normalized.Count - 1];
            if (!back.IsAccount || !string.Equals(back.Account, destination, StringComparison.Ordinal))
                normalized.Add(PathElement.OfAccount(destination));

            string strandSource = normalized[0].Account;
            string strandDestination = normalized[normalized.Count - 1].Account;
            if (normalized.Count < 2)
                return (hops, "temBAD_PATH", strandSource, strandDestination);

            for (int i = 0; i < normalized.Count - 1; i++)
            {
                PathElement cur = normalized[i];
                PathElement next = normalized[i + 1];

                // From an MPT to a currency: the currency's issuer comes from this element.
                if (current.IsMpt && cur.HasCurrency)
                    current = new PathAsset { Currency = "XRP", Issuer = string.Empty };

                // Only a currency's issuer moves; an MPT's is part of its issuance.
                if (!current.IsMpt)
                {
                    if (cur.IsAccount)
                        current.Issuer = cur.Account;
                    else if (cur.HasIssuer)
                        current.Issuer = cur.Issuer;
                }

                if (cur.HasCurrency)
                {
                    current = new PathAsset { Currency = cur.Currency, Issuer = current.Issuer };
                    if (PathElement.IsXrpCurrency(current.Currency))
                        current.Issuer = string.Empty;
                }
                else if (cur.HasMpt)
                {
                    current = new PathAsset { Mpt = cur.Mpt, Issuer = DexAssets.MptIssuer(cur.Mpt) };
                }

                if (cur.IsOffer && next.IsAccount)
                {
                    // After an offer the book's issuer hands over to the next account, if it is
                    // another. An offer selling an MPT is always followed by its issuer.
                    if (!string.Equals(current.Issuer, next.Account, StringComparison.Ordinal) && next.Account.Length != 0)
                    {
                        if (current.IsXrp)
                        {
                            if (i != normalized.Count - 2)
                                return (hops, "temBAD_PATH", strandSource, strandDestination);

                            hops.Add(new Hop { Kind = HopKind.XrpEndpoint, Source = next.Account });
                        }
                        else if (current.IsMpt)
                        {
                            // An MPT does not ripple.
                            return (hops, "temBAD_PATH", strandSource, strandDestination);
                        }
                        else
                        {
                            hops.Add(new Hop { Kind = HopKind.Direct, Source = current.Issuer, Destination = next.Account, Currency = current.Currency });
                        }
                    }

                    continue;
                }

                if (!next.IsOffer && next.HasAsset && !current.SameAsset(next))
                    return (hops, "temBAD_PATH", strandSource, strandDestination);

                bool isFirst = hops.Count == 0;
                bool isLast = i == normalized.Count - 2;
                if (isFirst && cur.IsAccount && cur.HasCurrency && PathElement.IsXrpCurrency(cur.Currency))
                {
                    hops.Add(new Hop { Kind = HopKind.XrpEndpoint, Source = cur.Account, IsLast = isLast });
                }
                else if (isLast && IsXrpAccount(cur) && next.IsAccount)
                {
                    hops.Add(new Hop { Kind = HopKind.XrpEndpoint, Source = next.Account, IsLast = true });
                }
                else if (cur.IsAccount && next.IsAccount)
                {
                    hops.Add(current.IsMpt
                        ? new Hop { Kind = HopKind.MptEndpoint, Source = cur.Account, Destination = next.Account, Mpt = current.Mpt, IsLast = isLast }
                        : new Hop { Kind = HopKind.Direct, Source = cur.Account, Destination = next.Account, Currency = current.Currency, IsLast = isLast });
                }
                else if (cur.IsOffer && next.IsAccount)
                {
                    return (hops, "temBAD_PATH", strandSource, strandDestination);
                }
                else
                {
                    PathAsset output = next.HasMpt
                        ? new PathAsset { Mpt = next.Mpt, Issuer = DexAssets.MptIssuer(next.Mpt) }
                        : next.HasCurrency
                            ? new PathAsset { Currency = next.Currency, Issuer = next.HasIssuer ? next.Issuer : current.Issuer }
                            : current.IsMpt
                                ? current
                                : new PathAsset { Currency = current.Currency, Issuer = next.HasIssuer ? next.Issuer : current.Issuer };
                    if (current.IsXrp && output.IsXrp)
                        return (hops, "temBAD_PATH", strandSource, strandDestination);

                    hops.Add(new Hop
                    {
                        Kind = HopKind.Book,
                        In = current.ToAsset(),
                        Out = output.ToAsset(),
                        IsLast = isLast,
                    });
                }
            }

            return (hops, null, strandSource, strandDestination);
        }

        /// <summary><c>toStrand</c>: one path; the result is null on success, otherwise the failure.</summary>
        internal static (string Result, List<FlowStep> Strand) ToStrand(DexView view, Request request, IReadOnlyList<PathElement> path)
        {
            (List<Hop> hops, string failure, string strandSource, string strandDestination) = Plan(request, path);
            bool isDefaultPath = path.Count == 0;

            List<FlowStep> strand = new List<FlowStep>();
            HashSet<AssetKey>[] seenDirectAssets = { new HashSet<AssetKey>(), new HashSet<AssetKey>() };
            HashSet<AssetKey> seenBookOuts = new HashSet<AssetKey>();
            foreach (Hop hop in hops)
            {
                StrandContext context = new StrandContext
                {
                    View = view,
                    StrandSource = strandSource,
                    StrandDestination = strandDestination,
                    StrandDeliver = request.Deliver,
                    LimitQuality = request.LimitQuality,
                    IsFirst = strand.Count == 0,
                    IsLast = hop.IsLast,
                    OfferCrossing = request.OfferCrossing,
                    IsDefaultPath = isDefaultPath,
                    PreviousStep = strand.Count == 0 ? null : strand[strand.Count - 1],
                    StrandSize = strand.Count,
                    SeenDirectAssets = seenDirectAssets,
                    SeenBookOuts = seenBookOuts,
                    AmmContext = request.AmmContext,
                    DomainId = request.DomainId,
                };

                (string result, FlowStep step) = hop.Kind switch
                {
                    HopKind.XrpEndpoint => XrpEndpoint(context, hop.Source),
                    HopKind.Direct => Direct(context, hop.Source, hop.Destination, hop.Currency),
                    HopKind.MptEndpoint => MptEndpoint(context, hop.Source, hop.Destination, hop.Mpt),
                    _ => Book(context, hop.In, hop.Out),
                };
                if (result != null)
                    return (result, null);

                strand.Add(step);
            }

            return failure != null ? (failure, null) : (null, strand);
        }

        private static (string Result, FlowStep Step) Book(StrandContext context, IssuedCurrency @in, IssuedCurrency @out)
        {
            BookCrossingStep book = new BookCrossingStep(context, @in, @out);
            string ter = book.Check(context);
            return ter == null ? (null, book) : (ter, null);
        }

        private static (string Result, FlowStep Step) Direct(StrandContext context, string source, string destination, string currency)
        {
            DirectStep step = new DirectStep(context, source, destination, currency);
            string ter = step.Check(context);
            return ter == null ? (null, step) : (ter, null);
        }

        private static (string Result, FlowStep Step) MptEndpoint(StrandContext context, string source, string destination, string mpt)
        {
            MptEndpointStep step = new MptEndpointStep(context, source, destination, mpt);
            string ter = step.Check(context);
            return ter == null ? (null, step) : (ter, null);
        }

        private static (string Result, FlowStep Step) XrpEndpoint(StrandContext context, string account)
        {
            // Offer crossing leaves the reserve of the trust line or MPToken the taker is about to get.
            int reserveReduction = 0;
            if (context.OfferCrossing && context.IsFirst)
            {
                IssuedCurrency deliver = context.StrandDeliver;
                bool holds = XrplAmount.KindOf(deliver) switch
                {
                    AmountKind.Iou => context.View.LineExists(account, deliver.Issuer, deliver.Currency),
                    AmountKind.Mpt => context.View.MpTokenExists(account, deliver),
                    _ => true,
                };
                if (!holds)
                    reserveReduction = -1;
            }

            XrpEndpointStep step = new XrpEndpointStep(context, account, reserveReduction);
            string ter = step.Check(context);
            return ter == null ? (null, step) : (ter, null);
        }

        private static string IssuerOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp ? string.Empty : asset.Issuer;

        /// <summary>Two strands with the same steps, as <c>Strand ==</c> compares them.</summary>
        private static bool SameStrand(List<FlowStep> a, List<FlowStep> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].GetType() != b[i].GetType())
                    return false;
                if (a[i].DirectStepAccounts != b[i].DirectStepAccounts)
                    return false;
                if (a[i].BookStepBook is { } x && b[i].BookStepBook is { } y &&
                    (AssetKey.Of(x.In) != AssetKey.Of(y.In) || AssetKey.Of(x.Out) != AssetKey.Of(y.Out)))
                {
                    return false;
                }

                if (a[i] is DirectStep d1 && b[i] is DirectStep d2 && d1.Currency != d2.Currency)
                    return false;
                if (a[i] is MptEndpointStep m1 && b[i] is MptEndpointStep m2 && !XrplAmount.SameAsset(m1.Mpt, m2.Mpt))
                    return false;
            }

            return true;
        }
    }
}
