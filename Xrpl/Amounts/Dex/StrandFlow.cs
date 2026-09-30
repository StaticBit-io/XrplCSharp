using System;
using System.Collections.Generic;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>rippled's <c>OfferCrossing</c>: a payment (<c>No</c>), offer crossing, or offer crossing with <c>tfSell</c>.</summary>
    internal enum CrossingMode
    {
        No,
        Yes,
        Sell,
    }

    /// <summary>What running one strand gave (<c>StrandResult</c>).</summary>
    internal sealed class StrandResult
    {
        internal bool Success { get; init; }

        internal XrplAmount In { get; init; }

        internal XrplAmount Out { get; init; }

        internal DexView Sandbox { get; init; }

        internal OfferRemovals OffersToRemove { get; init; }

        internal int OffersUsed { get; init; }

        internal bool Inactive { get; init; }
    }

    /// <summary>What the engine's loop over the strands gave (<c>FlowResult</c>).</summary>
    internal sealed class FlowResult
    {
        internal string Result { get; init; }

        internal XrplAmount In { get; init; }

        internal XrplAmount Out { get; init; }

        /// <summary>The state after the flow, when it succeeded.</summary>
        internal DexView Sandbox { get; init; }

        internal OfferRemovals RemovableOffers { get; init; }

        internal bool Succeeded => Result == "tesSUCCESS";
    }

    /// <summary>
    /// rippled's <c>StrandFlow.h</c>: each strand run backwards from the output and forwards from
    /// the limiting step, and the loop that takes the best strand's liquidity iteration by
    /// iteration until the request, the funds or the price is exhausted.
    /// </summary>
    internal static class StrandFlow
    {
        private const int MaxTries = 1000;
        private const int MaxOffersToConsider = 1500;

        /// <summary>The single-strand <c>flow</c>.</summary>
        internal static StrandResult Run(DexView baseView, List<FlowStep> strand, XrplAmount? maxIn, XrplAmount @out)
        {
            OfferRemovals offersToRemove = new OfferRemovals();
            StrandResult Failed() => new StrandResult { OffersToRemove = offersToRemove, OffersUsed = OffersUsed(strand) };

            // isDirectXrpToXrp: the engine does not move XRP straight from one account to another.
            if (strand.Count == 2 && @out.Kind == AmountKind.Xrp && strand[0] is XrpEndpointStep)
                return Failed();

            try
            {
                int count = strand.Count;
                int limitingStep = count;
                DexView sb = new DexView(baseView);
                DexView afView = new DexView(baseView);
                XrplAmount limitStepOut = default;

                XrplAmount stepOut = @out;
                for (int i = count - 1; i >= 0; i--)
                {
                    (XrplAmount In, XrplAmount Out) r = strand[i].Rev(sb, afView, offersToRemove, stepOut);
                    if (r.Out.IsZero)
                        return Failed();

                    if (i == 0 && maxIn is { } max && max < r.In)
                    {
                        // The input is limited: throw the reverse pass away and run this step forward.
                        sb = new DexView(baseView);
                        limitingStep = i;
                        r = strand[i].Fwd(sb, afView, offersToRemove, max);
                        limitStepOut = r.Out;
                        if (r.Out.IsZero || r.In != max)
                            return Failed();
                    }
                    else if (r.Out != stepOut)
                    {
                        // This step limits the strand: start again from what it can give.
                        sb = new DexView(baseView);
                        afView = new DexView(baseView);
                        limitingStep = i;
                        stepOut = r.Out;
                        r = strand[i].Rev(sb, afView, offersToRemove, stepOut);
                        limitStepOut = r.Out;
                        if (r.Out.IsZero || r.Out != stepOut)
                            return Failed();
                    }

                    stepOut = r.In;
                }

                XrplAmount stepIn = limitStepOut;
                for (int i = limitingStep + 1; i < count; i++)
                {
                    (XrplAmount In, XrplAmount Out) r = strand[i].Fwd(sb, afView, offersToRemove, stepIn);
                    if (r.Out.IsZero || r.In != stepIn)
                        return Failed();

                    stepIn = r.Out;
                }

                bool inactive = false;
                foreach (FlowStep step in strand)
                    inactive |= step.Inactive;

                return new StrandResult
                {
                    Success = true,
                    In = strand[0].CachedIn.Value,
                    Out = strand[count - 1].CachedOut.Value,
                    Sandbox = sb,
                    OffersToRemove = offersToRemove,
                    OffersUsed = OffersUsed(strand),
                    Inactive = inactive,
                };
            }
            catch (FlowFailedException)
            {
                return Failed();
            }
        }

        private static int OffersUsed(List<FlowStep> strand)
        {
            int used = 0;
            foreach (FlowStep step in strand)
                used += step.OffersUsed;
            return used;
        }

        /// <summary><c>qualityUpperBound</c>: the best quality a strand can give now.</summary>
        internal static XrplQuality? QualityUpperBound(DexView view, List<FlowStep> strand)
        {
            XrplQuality quality = new XrplQuality(FlowStep.QualityOneEncoded);
            DebtDirection direction = DebtDirection.Issues;
            foreach (FlowStep step in strand)
            {
                (XrplQuality? stepQuality, DebtDirection next) = step.QualityUpperBound(view, direction);
                direction = next;
                if (stepQuality == null)
                    return null;

                quality = BookCrossingStep.Composed(quality, stepQuality.Value, view.Rules);
            }

            return quality;
        }

        /// <summary>
        /// <c>limitOut</c>: on a single strand whose quality depends on its size - an AMM pool -
        /// the output that gives exactly the limit quality on average.
        /// </summary>
        private static XrplAmount LimitOut(DexView view, List<FlowStep> strand, XrplAmount remainingOut, XrplQuality limitQuality)
        {
            NumberContext c = view.Rules.Context;
            QualityFunction combined = null;
            DebtDirection direction = DebtDirection.Issues;
            foreach (FlowStep step in strand)
            {
                (QualityFunction function, DebtDirection next) = step.GetQualityFunc(view, direction);
                direction = next;
                if (function == null)
                    return remainingOut;

                if (combined == null)
                    combined = function;
                else
                    combined.Combine(function, c);
            }

            if (combined == null || combined.IsConst)
                return remainingOut;

            XrplNumber? target = combined.OutFromAvgQ(limitQuality, c);
            XrplAmount limited;
            if (target == null)
            {
                limited = remainingOut;
            }
            else if (remainingOut.IsIntegral)
            {
                limited = StepMath.ToAmount(remainingOut.Asset, target.Value, c.Rounding, c.Rounding);
                if (view.Rules.MPTokensV2 && limited.Value > target.Value && !combined.SatisfiesAvgQ(limitQuality, limited.Value, c))
                    limited = StepMath.ToAmount(remainingOut.Asset, target.Value, NumberRounding.Downward, NumberRounding.Downward);
            }
            else
            {
                limited = XrplAmount.FromNumber(remainingOut.Asset, target.Value, c.Rounding);
            }

            // A tiny difference is round-off.
            if (WithinRelativeDistance(limited, remainingOut, new XrplNumber(1, -9), view.Rules))
                return remainingOut;

            return StepMath.Min(limited, remainingOut);
        }

        private static bool WithinRelativeDistance(XrplAmount calculated, XrplAmount requested, XrplNumber distance, LedgerRules rules)
        {
            if (calculated == requested)
                return true;

            XrplAmount min = calculated < requested ? calculated : requested;
            XrplAmount max = calculated < requested ? requested : calculated;
            XrplAmount difference = StepMath.Subtract(max, min, rules);
            return XrplNumber.Divide(difference.Value, max.Value, rules.Context) < distance;
        }

        /// <summary>The multi-strand <c>flow</c>.</summary>
        internal static FlowResult Run(
            DexView baseView,
            List<List<FlowStep>> strands,
            IssuedCurrency inAsset,
            XrplAmount outRequested,
            bool partialPayment,
            CrossingMode crossing,
            XrplQuality? limitQuality,
            XrplAmount? sendMax,
            AmmFlowContext ammContext)
        {
            LedgerRules rules = baseView.Rules;
            int currentTry = 0;
            int offersConsidered = 0;

            // Inside the engine amounts are typed: an issued currency carries no issuer.
            IssuedCurrency outAsset = outRequested.Asset;
            outRequested = StepMath.Typed(outRequested);
            IssuedCurrency typedIn = inAsset;
            if (XrplAmount.KindOf(inAsset) == AmountKind.Iou)
                typedIn = StepMath.TypedIou(inAsset.Currency);

            XrplAmount? remainingIn = sendMax is { } max && !max.IsNegative ? StepMath.Typed(max) : null;
            XrplAmount? sendMaxLimit = remainingIn;
            XrplAmount remainingOut = outRequested;
            DexView sb = new DexView(baseView);

            List<List<FlowStep>> current = new List<List<FlowStep>>();
            List<List<FlowStep>> next = new List<List<FlowStep>>(strands);

            List<XrplAmount> savedIns = new List<XrplAmount>();
            List<XrplAmount> savedOuts = new List<XrplAmount>();
            OfferRemovals offersToRemoveOnFail = new OfferRemovals();

            while (StepMath.IsPositive(remainingOut) && (remainingIn == null || StepMath.IsPositive(remainingIn.Value)))
            {
                if (++currentTry >= MaxTries)
                    return new FlowResult { Result = "telFAILED_PROCESSING", RemovableOffers = offersToRemoveOnFail };

                void Tried(List<FlowStep> tried, FlowPassOutcome outcome, XrplAmount? triedIn = null, XrplAmount? triedOut = null)
                {
                    if (sb.World.RecordPasses)
                        sb.World.Passes.Add(new FlowPass(currentTry, strands.IndexOf(tried), outcome, triedIn, triedOut));
                }

                ActivateNext(sb, limitQuality, current, next, Tried);
                ammContext.MultiPath = current.Count > 1;

                XrplAmount limitRemainingOut = current.Count == 1 && limitQuality is { } limitQ
                    ? LimitOut(sb, current[0], remainingOut, limitQ)
                    : remainingOut;
                bool adjustedRemainingOut = limitRemainingOut != remainingOut;

                OfferRemovals offersToRemove = new OfferRemovals();
                StrandResult best = null;
                int bestStrand = -1;

                for (int index = 0; index < current.Count; index++)
                {
                    List<FlowStep> strand = current[index];
                    ammContext.Clear();

                    if (crossing != CrossingMode.No && limitQuality is { } threshold)
                    {
                        XrplQuality? upperBound = QualityUpperBound(sb, strand);
                        if (upperBound == null || upperBound.Value < threshold)
                        {
                            Tried(strand, FlowPassOutcome.OutOfReach);
                            continue;
                        }
                    }

                    StrandResult f = Run(sb, strand, remainingIn, limitRemainingOut);
                    offersToRemove.Merge(f.OffersToRemove);
                    offersConsidered += f.OffersUsed;
                    if (!f.Success || f.Out.IsZero)
                    {
                        Tried(strand, FlowPassOutcome.Dry);
                        continue;
                    }

                    XrplQuality quality = XrplQuality.FromAmounts(f.In, f.Out, rules);
                    if (limitQuality is { } limit && quality < limit &&
                        (!adjustedRemainingOut || !XrplQuality.WithinRelativeDistance(quality, limit, new XrplNumber(1, -7), rules)))
                    {
                        Tried(strand, FlowPassOutcome.BelowLimitQuality, f.In.WithAsset(inAsset), f.Out.WithAsset(outAsset));
                        continue;
                    }

                    Tried(strand, FlowPassOutcome.Taken, f.In.WithAsset(inAsset), f.Out.WithAsset(outAsset));
                    bestStrand = strands.IndexOf(strand);
                    if (!f.Inactive)
                        next.Add(strand);
                    best = f;
                    for (int rest = index + 1; rest < current.Count; rest++)
                        next.Add(current[rest]);
                    break;
                }

                bool shouldBreak = best == null || offersConsidered >= MaxOffersToConsider;
                if (best != null)
                {
                    savedIns.Add(best.In);
                    savedOuts.Add(best.Out);
                    remainingOut = StepMath.Subtract(outRequested, StepMath.Sum(savedOuts, outRequested.Asset, rules), rules);
                    if (sendMaxLimit is { } maxIn)
                        remainingIn = StepMath.Subtract(maxIn, StepMath.Sum(savedIns, typedIn, rules), rules);

                    foreach (FillRecord fill in best.Sandbox.OwnFills)
                    {
                        fill.Pass = currentTry;
                        fill.Strand = bestStrand;
                    }

                    best.Sandbox.ApplyTo(sb);
                    ammContext.Update();
                }

                if (offersToRemove.Count > 0)
                {
                    offersToRemoveOnFail.Merge(offersToRemove);
                    foreach ((string index, OfferRemovalReason reason) in offersToRemove)
                    {
                        if (!sb.Offer(index).Deleted)
                            sb.DeleteOffer(index, reason);
                    }
                }

                if (shouldBreak)
                    break;
            }

            XrplAmount actualOutTyped = StepMath.Sum(savedOuts, outRequested.Asset, rules);
            XrplAmount actualOut = actualOutTyped.WithAsset(outAsset);
            XrplAmount actualIn = StepMath.Sum(savedIns, typedIn, rules).WithAsset(inAsset);

            if (actualOutTyped != outRequested)
            {
                if (actualOutTyped > outRequested)
                    return new FlowResult { Result = "tefEXCEPTION", RemovableOffers = offersToRemoveOnFail };

                if (!partialPayment)
                {
                    // Offer crossing without partial payment is fill-or-kill; with tfSell it is handled below.
                    if (crossing == CrossingMode.No || (rules.FixFillOrKill && crossing != CrossingMode.Sell))
                        return new FlowResult { Result = "tecPATH_PARTIAL", In = actualIn, Out = actualOut, RemovableOffers = offersToRemoveOnFail };
                }
                else if (actualOutTyped.IsZero)
                {
                    return new FlowResult { Result = "tecPATH_DRY", RemovableOffers = offersToRemoveOnFail };
                }
            }

            if (crossing != CrossingMode.No && !partialPayment && (!rules.FixFillOrKill || crossing == CrossingMode.Sell) &&
                remainingIn is { } left && !left.IsZero)
            {
                return new FlowResult { Result = "tecPATH_PARTIAL", In = actualIn, Out = actualOut, RemovableOffers = offersToRemoveOnFail };
            }

            return new FlowResult
            {
                Result = "tesSUCCESS",
                In = actualIn,
                Out = actualOut,
                Sandbox = sb,
                RemovableOffers = offersToRemoveOnFail,
            };
        }

        /// <summary>
        /// <c>ActiveStrands::activateNext</c>: the strands still in play, best estimated quality
        /// first; one worse than the limit is dropped for good.
        /// </summary>
        private static void ActivateNext(
            DexView view,
            XrplQuality? limitQuality,
            List<List<FlowStep>> current,
            List<List<FlowStep>> next,
            Action<List<FlowStep>, FlowPassOutcome, XrplAmount?, XrplAmount?> dropped)
        {
            current.Clear();
            if (next.Count > 1)
            {
                List<(XrplQuality Quality, List<FlowStep> Strand)> ranked = new List<(XrplQuality, List<FlowStep>)>();
                foreach (List<FlowStep> strand in next)
                {
                    XrplQuality? upperBound = QualityUpperBound(view, strand);
                    if (upperBound is not { } quality)
                        dropped(strand, FlowPassOutcome.Dry, null, null);
                    else if (limitQuality is { } limit && quality < limit)
                        dropped(strand, FlowPassOutcome.OutOfReach, null, null);
                    else
                        ranked.Add((quality, strand));
                }

                // A stable sort, better quality first.
                for (int i = 1; i < ranked.Count; i++)
                {
                    (XrplQuality Quality, List<FlowStep> Strand) item = ranked[i];
                    int j = i - 1;
                    while (j >= 0 && item.Quality > ranked[j].Quality)
                    {
                        ranked[j + 1] = ranked[j];
                        j--;
                    }

                    ranked[j + 1] = item;
                }

                next.Clear();
                foreach ((XrplQuality _, List<FlowStep> strand) in ranked)
                    next.Add(strand);
            }

            current.AddRange(next);
            next.Clear();
        }
    }
}
