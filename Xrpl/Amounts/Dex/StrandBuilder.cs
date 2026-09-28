using System;
using System.Collections.Generic;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// One element of a payment path (<c>STPathElement</c>): an account, or an offer book named by
    /// its output currency, its issuer, or both. XRP is the currency <c>"XRP"</c>, and its issuer
    /// or account is the empty string.
    /// </summary>
    internal readonly struct PathElement
    {
        internal PathElement(string account, string currency, string issuer)
        {
            Account = account;
            Currency = currency;
            Issuer = issuer;
        }

        internal string Account { get; }

        internal string Currency { get; }

        internal string Issuer { get; }

        internal bool IsAccount => Account != null;

        internal bool IsOffer => Account == null;

        internal bool HasCurrency => Currency != null;

        internal bool HasIssuer => Issuer != null;

        internal bool IsEmpty => Account == null && Currency == null && Issuer == null;

        internal static PathElement OfAccount(string account) => new PathElement(account, null, null);

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
        }

        /// <summary>A step as <c>toStrand</c> lays it out: the accounts and assets, and whether it ends the strand.</summary>
        internal sealed class Hop
        {
            internal HopKind Kind { get; init; }

            /// <summary>A direct step's source, or the account of an XRP step.</summary>
            internal string Source { get; init; }

            internal string Destination { get; init; }

            internal string Currency { get; init; }

            internal IssuedCurrency In { get; init; }

            internal IssuedCurrency Out { get; init; }

            internal bool IsLast { get; init; }
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

            foreach (PathElement element in path)
            {
                bool bad = element.IsEmpty ||
                           (element.IsAccount && (element.HasIssuer || element.HasCurrency)) ||
                           (element.HasIssuer && element.Issuer.Length == 0) ||
                           (element.IsAccount && element.Account.Length == 0) ||
                           (element.HasCurrency && element.HasIssuer && PathElement.IsXrpCurrency(element.Currency) != (element.Issuer.Length == 0));
                if (bad)
                    return (hops, "temBAD_PATH", source, destination);
            }

            IssuedCurrency start = request.SendMax ?? deliver;
            (string Currency, string Issuer) current = XrplAmount.KindOf(start) == AmountKind.Xrp
                ? ("XRP", string.Empty)
                : (start.Currency, source);

            // The source first, as the transaction's Account, in the SendMax or the delivered asset.
            List<PathElement> normalized = new List<PathElement> { new PathElement(source, current.Currency, current.Issuer) };

            // SendMax's issuer, when it is not the source, is the implied second hop - unless the
            // path starts at it.
            string sendMaxIssuer = request.SendMax == null ? null : IssuerOf(request.SendMax);
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
                if (normalized[i].HasCurrency)
                {
                    lastAsset = normalized[i];
                    break;
                }
            }

            string deliverCurrency = XrplAmount.KindOf(deliver) == AmountKind.Xrp ? "XRP" : deliver.Currency;
            string deliverIssuer = IssuerOf(deliver);
            if (!string.Equals(lastAsset.Currency, deliverCurrency, StringComparison.Ordinal) ||
                (request.OfferCrossing && !string.Equals(lastAsset.Issuer, deliverIssuer, StringComparison.Ordinal)))
            {
                normalized.Add(new PathElement(null, deliverCurrency, deliverIssuer));
            }

            // The delivered asset's issuer, when it is not the destination, is the implied
            // second-to-last hop.
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

                if (cur.IsAccount)
                    current.Issuer = cur.Account;
                else if (cur.HasIssuer)
                    current.Issuer = cur.Issuer;

                if (cur.HasCurrency)
                {
                    current.Currency = cur.Currency;
                    if (PathElement.IsXrpCurrency(current.Currency))
                        current.Issuer = string.Empty;
                }

                if (cur.IsOffer && next.IsAccount)
                {
                    // After an offer the book's issuer hands over to the next account, if it is another.
                    if (!string.Equals(current.Issuer, next.Account, StringComparison.Ordinal) && next.Account.Length != 0)
                    {
                        if (PathElement.IsXrpCurrency(current.Currency))
                        {
                            if (i != normalized.Count - 2)
                                return (hops, "temBAD_PATH", strandSource, strandDestination);

                            hops.Add(new Hop { Kind = HopKind.XrpEndpoint, Source = next.Account });
                        }
                        else
                        {
                            hops.Add(new Hop { Kind = HopKind.Direct, Source = current.Issuer, Destination = next.Account, Currency = current.Currency });
                        }
                    }

                    continue;
                }

                if (!next.IsOffer && next.HasCurrency && !string.Equals(next.Currency, current.Currency, StringComparison.Ordinal))
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
                    hops.Add(new Hop { Kind = HopKind.Direct, Source = cur.Account, Destination = next.Account, Currency = current.Currency, IsLast = isLast });
                }
                else if (cur.IsOffer && next.IsAccount)
                {
                    return (hops, "temBAD_PATH", strandSource, strandDestination);
                }
                else
                {
                    string outCurrency = next.HasCurrency ? next.Currency : current.Currency;
                    string outIssuer = next.HasIssuer ? next.Issuer : current.Issuer;
                    bool currentXrp = PathElement.IsXrpCurrency(current.Currency);
                    bool outXrp = PathElement.IsXrpCurrency(outCurrency);
                    if (currentXrp && outXrp)
                        return (hops, "temBAD_PATH", strandSource, strandDestination);

                    IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
                    hops.Add(new Hop
                    {
                        Kind = HopKind.Book,
                        In = currentXrp ? xrp : new IssuedCurrency { Currency = current.Currency, Issuer = current.Issuer },
                        Out = outXrp ? xrp : new IssuedCurrency { Currency = outCurrency, Issuer = outIssuer },
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
                };

                (string result, FlowStep step) = hop.Kind switch
                {
                    HopKind.XrpEndpoint => XrpEndpoint(context, hop.Source),
                    HopKind.Direct => Direct(context, hop.Source, hop.Destination, hop.Currency),
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

        private static (string Result, FlowStep Step) XrpEndpoint(StrandContext context, string account)
        {
            // Offer crossing leaves the reserve of the trust line the taker is about to get.
            int reserveReduction = 0;
            if (context.OfferCrossing && context.IsFirst && XrplAmount.KindOf(context.StrandDeliver) == AmountKind.Iou &&
                !context.View.LineExists(account, context.StrandDeliver.Issuer, context.StrandDeliver.Currency))
            {
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
            }

            return true;
        }
    }
}
