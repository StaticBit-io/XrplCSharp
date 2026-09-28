using System;
using System.Collections.Generic;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// rippled's <c>BookStep</c>: one order book walked the way the payment engine walks it - the
    /// AMM pool first, then the order book's offers of one quality per pass, each funded,
    /// fee-adjusted and cut to what the strand still needs, and the stale ones on the way removed.
    /// Offer crossing (<c>BookOfferCrossingStep</c>) has the owner pay the transfer fee on what it
    /// gives and prunes by the taker's price; a payment (<c>BookPaymentStep</c>) leaves the fee to
    /// the next step and takes any quality. A book in a permissioned domain holds the domain's
    /// offers, hybrid ones included, and no AMM pool.
    /// </summary>
    internal sealed class BookCrossingStep : FlowStep
    {
        private const int MaxOffersToConsume = 1000;

        private readonly IssuedCurrency _in;
        private readonly IssuedCurrency _out;
        private readonly string _domain;
        private readonly string _strandSource;
        private readonly string _strandDestination;
        private readonly FlowStep _previous;
        private readonly bool _defaultPath;
        private readonly bool _offerCrossing;
        private readonly XrplQuality _qualityThreshold;
        private readonly AmmBookLiquidity _amm;
        private (XrplAmount In, XrplAmount Out)? _cache;
        private bool _inactive;
        private int _offersUsed;

        internal BookCrossingStep(StrandContext context, IssuedCurrency @in, IssuedCurrency @out)
        {
            _in = @in;
            _out = @out;
            _domain = BookKey.DomainKey(context.DomainId);
            _strandSource = context.StrandSource;
            _strandDestination = context.StrandDestination;
            _previous = context.PreviousStep;
            _defaultPath = context.IsDefaultPath;
            _offerCrossing = context.OfferCrossing;
            if (_offerCrossing)
                _qualityThreshold = context.LimitQuality ?? throw new FlowFailedException("tefINTERNAL", "Offer requires quality.");

            DexAmmPool pool = context.View.World.Pool(@in, @out);
            if (pool != null)
                _amm = new AmmBookLiquidity(context.View, pool, @in, @out, context.AmmContext);
        }

        internal override XrplAmount? CachedIn => _cache is { } cache ? StepMath.Typed(cache.In) : null;

        internal override XrplAmount? CachedOut => _cache is { } cache ? StepMath.Typed(cache.Out) : null;

        internal override (IssuedCurrency In, IssuedCurrency Out)? BookStepBook => (_in, _out);

        internal override int OffersUsed => _offersUsed;

        internal override bool Inactive => _inactive;

        /// <summary>The offer owner pays the transfer fee when offer crossing, so the step issues; a payment's step redeems.</summary>
        internal override DebtDirection DebtDirection(DexView sb, StrandDirection direction) =>
            _offerCrossing ? Amounts.DebtDirection.Issues : Amounts.DebtDirection.Redeems;

        // ---- quality estimates ----

        internal override (XrplQuality? Quality, DebtDirection Direction) QualityUpperBound(DexView view, DebtDirection previous)
        {
            DebtDirection direction = DebtDirection(view, StrandDirection.Forward);
            object tip = Tip(view);
            if (tip == null)
                return (null, direction);

            return tip is AmmBookOffer amm
                ? (AdjustQualityWithFees(view, amm.Quality, previous, isAmm: true), direction)
                : (AdjustQualityWithFees(view, (XrplQuality)tip, previous, isAmm: false), direction);
        }

        internal override (QualityFunction Function, DebtDirection Direction) GetQualityFunc(DexView view, DebtDirection previous)
        {
            DebtDirection direction = DebtDirection(view, StrandDirection.Forward);
            object tip = Tip(view);
            if (tip == null)
                return (null, direction);

            NumberContext c = view.Rules.Context;
            if (tip is AmmBookOffer amm)
            {
                QualityFunction curve = amm.QualityFunction(view.Rules);
                if (curve.IsConst)
                    return (QualityFunction.ClobLike(AdjustQualityWithFees(view, curve.Quality.Value, previous, isAmm: false), c), direction);

                XrplQuality one = new XrplQuality(QualityOneEncoded);
                XrplQuality fees = AdjustQualityWithFees(view, one, previous, isAmm: true);
                if (fees == one)
                    return (curve, direction);

                QualityFunction combined = QualityFunction.ClobLike(fees, c);
                combined.Combine(curve, c);
                return (combined, direction);
            }

            return (QualityFunction.ClobLike(AdjustQualityWithFees(view, (XrplQuality)tip, previous, isAmm: false), c), direction);
        }

        /// <summary>
        /// <c>tip</c>: the order book's best quality, or the pool's offer when it is strictly
        /// better. Unfunded and expired offers count here; only the walk removes them.
        /// </summary>
        private object Tip(DexView view)
        {
            XrplQuality? book = null;
            foreach (DexOffer offer in Offers(view.World))
            {
                if (!view.Offer(offer.Index).Deleted)
                {
                    book = offer.Quality;
                    break;
                }
            }

            if (book == null && IsPartial(view.World))
                view.World.ReachedPartialBook = true;

            XrplQuality? threshold = view.Rules.FixAMMv1_1 && book is { } lob ? QualityThresholdForAmm(lob) : null;
            AmmBookOffer amm = AmmOffer(view, threshold);
            if (amm != null && (book == null || amm.Quality > book.Value))
                return amm;

            return book;
        }

        private IReadOnlyList<DexOffer> Offers(DexWorld world) => world.Book(_in, _out, _domain);

        private bool IsPartial(DexWorld world) => world.PartialBooks.Contains(BookKey.Of(_in, _out, _domain));

        /// <summary><c>getAMMOffer</c>: none for a domain book, which the pool does not serve, under <c>fixCleanup3_3_0</c>.</summary>
        private AmmBookOffer AmmOffer(DexView view, XrplQuality? threshold)
        {
            if (_domain.Length != 0 && view.Rules.FixCleanup3_3_0)
                return null;

            return _amm?.GetOffer(view, threshold);
        }

        /// <summary>
        /// <c>adjustQualityWithFees</c>. A payment composes the input's transfer fee, charged when
        /// the previous step redeems, with the offer's quality. Offer crossing leaves the quality
        /// alone, except for a single-path AMM offer under <c>fixAMMv1_1</c>.
        /// </summary>
        private XrplQuality AdjustQualityWithFees(DexView view, XrplQuality offerQuality, DebtDirection previous, bool isAmm)
        {
            if (_offerCrossing && (!view.Rules.FixAMMv1_1 || !isAmm || (_amm != null && _amm.MultiPath)))
                return offerQuality;

            uint rateIn = previous == Amounts.DebtDirection.Redeems ? view.Rate(_in, _strandDestination) : OfferCrossing.QualityOne;

            // A payment's offer owner does not pay the fee on what it gives, and a pool never does.
            return Composed(QualityOfRates(rateIn, OfferCrossing.QualityOne, view.Rules), offerQuality, view.Rules);
        }

        /// <summary><c>composedQuality</c>: the product of two rates, rounded up.</summary>
        internal static XrplQuality Composed(XrplQuality left, XrplQuality right, LedgerRules rules)
        {
            XrplAmount rate = XrplAmountMath.MulRound(left.RateAmount, right.RateAmount, null, roundUp: true, rules);
            ulong exponent = (ulong)(rate.StExponent + 100);
            return new XrplQuality((exponent << 56) | rate.StMantissa);
        }

        /// <summary>
        /// <c>qualityThreshold</c>: on a single path, an order book worse than the taker's limit
        /// does not size the pool's offer - the offer is sized to the limit instead.
        /// </summary>
        private XrplQuality? QualityThresholdForAmm(XrplQuality lobQuality)
        {
            if (_offerCrossing && _amm != null && !_amm.MultiPath && _qualityThreshold > lobQuality)
                return null;

            return lobQuality;
        }

        // ---- the walk ----

        private delegate bool OfferCallback(
            FlowOffer offer,
            (XrplAmount In, XrplAmount Out) offerAmount,
            (XrplAmount In, XrplAmount Out) stepAmount,
            XrplAmount ownerGives,
            uint rateIn,
            uint rateOut);

        /// <summary><c>forEachOffer</c>: the pool, then the order book's offers, one quality at most.</summary>
        private (HashSet<string> ToRemove, int Count) ForEachOffer(DexView sb, DexView afView, DebtDirection previous, OfferCallback callback)
        {
            LedgerRules rules = sb.Rules;
            uint rateIn = previous == Amounts.DebtDirection.Redeems ? sb.Rate(_in, _strandDestination) : OfferCrossing.QualityOne;
            uint rateOut = _offerCrossing ? sb.Rate(_out, _strandDestination) : OfferCrossing.QualityOne;

            OfferStream offers = new OfferStream(sb, afView, Offers(sb.World), MaxOffersToConsume, _domain, IsPartial(sb.World));
            bool offerAttempted = false;
            XrplQuality? offerQuality = null;

            bool ExecOffer(FlowOffer offer)
            {
                if (offerQuality == null)
                    offerQuality = offer.Quality;
                else if (offerQuality.Value != offer.Quality)
                    return false;

                // An offer of the taker's own at or better than its price is deleted, not crossed.
                if (_offerCrossing && _defaultPath && offer.Quality >= _qualityThreshold &&
                    string.Equals(_strandSource, offer.Owner, StringComparison.Ordinal) &&
                    string.Equals(_strandDestination, offer.Owner, StringComparison.Ordinal))
                {
                    if (offer.Key != null)
                        offers.PermanentlyRemove(offer.Key);
                    if (!offerAttempted)
                        offerQuality = null;
                    return true;
                }

                // An owner the issuer has not authorized to hold what it buys loses the offer.
                DexView authView = rules.MPTokensV2 ? sb : afView;
                if (authView.RequireAuth(offer.Owner, _in) != null)
                {
                    if (offer.Key != null)
                        offers.PermanentlyRemove(offer.Key);
                    if (!offerAttempted)
                        offerQuality = null;
                    return true;
                }

                if (_offerCrossing && _defaultPath && !(offer.Quality >= _qualityThreshold))
                    return false;

                // Offer crossing does not charge the taker a fee to pay itself.
                string sourceAccount = _previous?.DirectStepSourceAccount;
                uint offerRateIn = _offerCrossing && string.Equals(offer.Owner, sourceAccount, StringComparison.Ordinal)
                    ? OfferCrossing.QualityOne
                    : rateIn;
                uint offerRateOut = _offerCrossing && _previous != null && _previous.IsBookStep &&
                                    string.Equals(offer.Owner, _strandDestination, StringComparison.Ordinal)
                    ? OfferCrossing.QualityOne
                    : rateOut;
                (offerRateIn, offerRateOut) = offer.AdjustRates(offerRateIn, offerRateOut);

                (XrplAmount In, XrplAmount Out) offerAmount = (offer.In, offer.Out);
                XrplAmount stepIn = OfferCrossing.MulRatio(offerAmount.In, offerRateIn, OfferCrossing.QualityOne, roundUp: true, rules);
                XrplAmount stepOut = offerAmount.Out;
                XrplAmount ownerGives = OfferCrossing.MulRatio(offerAmount.Out, offerRateOut, OfferCrossing.QualityOne, roundUp: offerAmount.Out.Kind == AmountKind.Mpt, rules);

                XrplAmount funds = offer.IsFunded ? ownerGives : offers.OwnerFunds.Value;
                if (funds < ownerGives)
                {
                    ownerGives = funds;
                    stepOut = OfferCrossing.MulRatio(ownerGives, OfferCrossing.QualityOne, offerRateOut, roundUp: false, rules);

                    // Rounded down, strictly, so what is left of the offer is no worse than its page.
                    offerAmount = offer.LimitOut(offerAmount, stepOut, roundUp: false, rules);
                    stepIn = OfferCrossing.MulRatio(offerAmount.In, offerRateIn, OfferCrossing.QualityOne, roundUp: true, rules);
                }

                offerAttempted = true;
                return callback(offer, offerAmount, (stepIn, stepOut), ownerGives, offerRateIn, offerRateOut);
            }

            bool TryAmm(XrplQuality? lobQuality)
            {
                // The pool does not serve a domain book.
                if (_domain.Length != 0)
                    return true;

                XrplQuality? threshold = rules.FixAMMv1_1 && lobQuality is { } lob ? QualityThresholdForAmm(lob) : lobQuality;
                AmmBookOffer amm = AmmOffer(sb, threshold);
                return amm == null || ExecOffer(amm);
            }

            if (offers.Step())
            {
                if (TryAmm(offers.Tip.Quality))
                {
                    do
                    {
                        if (!ExecOffer(offers.Tip))
                            break;
                    }
                    while (offers.Step());
                }
            }
            else
            {
                TryAmm(null);
            }

            return (offers.ToRemove, offers.Count);
        }

        /// <summary><c>limitStepIn</c>: the step cut to take at most <paramref name="limit"/>.</summary>
        private static void LimitStepIn(
            FlowOffer offer,
            ref (XrplAmount In, XrplAmount Out) offerAmount,
            ref (XrplAmount In, XrplAmount Out) stepAmount,
            ref XrplAmount ownerGives,
            uint rateIn,
            uint rateOut,
            XrplAmount limit,
            LedgerRules rules)
        {
            if (!(limit < stepAmount.In))
                return;

            stepAmount.In = limit;
            XrplAmount inLimit = OfferCrossing.MulRatio(stepAmount.In, OfferCrossing.QualityOne, rateIn, roundUp: false, rules);
            offerAmount = offer.LimitIn(offerAmount, inLimit, roundUp: false, rules);
            stepAmount.Out = offerAmount.Out;
            ownerGives = OfferCrossing.MulRatio(offerAmount.Out, rateOut, OfferCrossing.QualityOne, roundUp: offerAmount.Out.Kind == AmountKind.Mpt, rules);
        }

        /// <summary><c>limitStepOut</c>: the step cut to give at most <paramref name="limit"/>.</summary>
        private static void LimitStepOut(
            FlowOffer offer,
            ref (XrplAmount In, XrplAmount Out) offerAmount,
            ref (XrplAmount In, XrplAmount Out) stepAmount,
            ref XrplAmount ownerGives,
            uint rateIn,
            uint rateOut,
            XrplAmount limit,
            LedgerRules rules)
        {
            if (!(limit < stepAmount.Out))
                return;

            stepAmount.Out = limit;
            ownerGives = OfferCrossing.MulRatio(stepAmount.Out, rateOut, OfferCrossing.QualityOne, roundUp: limit.Kind == AmountKind.Mpt, rules);
            offerAmount = offer.LimitOut(offerAmount, stepAmount.Out, roundUp: true, rules);
            stepAmount.In = OfferCrossing.MulRatio(offerAmount.In, rateIn, OfferCrossing.QualityOne, roundUp: true, rules);
        }

        /// <summary><c>consumeOffer</c>: the owner is paid, pays, and the offer shrinks.</summary>
        private void ConsumeOffer(
            DexView sb,
            FlowOffer offer,
            (XrplAmount In, XrplAmount Out) offerAmount,
            XrplAmount ownerGives)
        {
            if (!offer.CheckInvariant(offerAmount.In, offerAmount.Out, sb.Rules))
                throw new FlowFailedException("tecINVARIANT_FAILED", "AMM pool product invariant failed.");

            sb.Send(IssuerOf(_in), offer.Owner, offerAmount.In);
            sb.Send(offer.Owner, IssuerOf(_out), ownerGives);
            offer.Consume(sb, offerAmount.In, offerAmount.Out);
        }

        private static string IssuerOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp ? string.Empty : asset.Issuer;

        internal override (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @out)
        {
            @out = @out.WithAsset(_out);
            _cache = null;
            LedgerRules rules = sb.Rules;
            XrplAmount resultIn = XrplAmount.Zero(_in);
            XrplAmount resultOut = XrplAmount.Zero(_out);
            XrplAmount remainingOut = @out;
            List<XrplAmount> savedIns = new List<XrplAmount>();
            List<XrplAmount> savedOuts = new List<XrplAmount>();

            bool EachOffer(
                FlowOffer offer,
                (XrplAmount In, XrplAmount Out) offerAmount,
                (XrplAmount In, XrplAmount Out) stepAmount,
                XrplAmount ownerGives,
                uint rateIn,
                uint rateOut)
            {
                if (StepMath.IsNotPositive(remainingOut))
                    return false;

                if (stepAmount.Out <= remainingOut)
                {
                    savedIns.Add(stepAmount.In);
                    savedOuts.Add(stepAmount.Out);
                    resultIn = StepMath.Sum(savedIns, _in, rules);
                    resultOut = StepMath.Sum(savedOuts, _out, rules);
                    remainingOut = StepMath.Subtract(@out, resultOut, rules);
                    ConsumeOffer(sb, offer, offerAmount, ownerGives);

                    // Even when the request is met, the offer is consumed.
                    return true;
                }

                (XrplAmount In, XrplAmount Out) offerAdjusted = offerAmount;
                (XrplAmount In, XrplAmount Out) stepAdjusted = stepAmount;
                XrplAmount ownerGivesAdjusted = ownerGives;
                LimitStepOut(offer, ref offerAdjusted, ref stepAdjusted, ref ownerGivesAdjusted, rateIn, rateOut, remainingOut, rules);
                remainingOut = XrplAmount.Zero(_out);
                savedIns.Add(stepAdjusted.In);
                savedOuts.Add(remainingOut);
                resultIn = StepMath.Sum(savedIns, _in, rules);
                resultOut = @out;
                ConsumeOffer(sb, offer, offerAdjusted, ownerGivesAdjusted);

                // Two issued-currency amounts whose mantissas differ by less than ten subtract to
                // zero, so the offer may be used up even though it gave more than was left.
                return offer.FullyConsumed;
            }

            DebtDirection previous = _previous?.DebtDirection(sb, StrandDirection.Reverse) ?? Amounts.DebtDirection.Issues;
            (HashSet<string> toRemove, int consumed) = ForEachOffer(sb, afView, previous, EachOffer);
            _offersUsed = consumed;
            offersToRemove.UnionWith(toRemove);
            if (consumed >= MaxOffersToConsume)
                _inactive = true;

            if (remainingOut.IsNegative)
            {
                _cache = (XrplAmount.Zero(_in), XrplAmount.Zero(_out));
                return (StepMath.Typed(_cache.Value.In), StepMath.Typed(_cache.Value.Out));
            }

            if (remainingOut.IsZero)
                resultOut = @out;

            _cache = (resultIn, resultOut);
            return (StepMath.Typed(resultIn), StepMath.Typed(resultOut));
        }

        internal override (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @in)
        {
            @in = @in.WithAsset(_in);
            if (_cache == null)
                throw new InvalidOperationException("BookStep::fwdImp : cache is not set");

            (XrplAmount In, XrplAmount Out) reverse = _cache.Value;
            LedgerRules rules = sb.Rules;
            XrplAmount resultIn = XrplAmount.Zero(_in);
            XrplAmount resultOut = XrplAmount.Zero(_out);
            XrplAmount remainingIn = @in;
            List<XrplAmount> savedIns = new List<XrplAmount>();
            List<XrplAmount> savedOuts = new List<XrplAmount>();

            bool EachOffer(
                FlowOffer offer,
                (XrplAmount In, XrplAmount Out) offerAmount,
                (XrplAmount In, XrplAmount Out) stepAmount,
                XrplAmount ownerGives,
                uint rateIn,
                uint rateOut)
            {
                if (StepMath.IsNotPositive(remainingIn))
                    return false;

                bool processMore;
                (XrplAmount In, XrplAmount Out) offerAdjusted = offerAmount;
                (XrplAmount In, XrplAmount Out) stepAdjusted = stepAmount;
                XrplAmount ownerGivesAdjusted = ownerGives;

                List<XrplAmount> insAdjusted = new List<XrplAmount>(savedIns);
                List<XrplAmount> outsAdjusted = new List<XrplAmount>(savedOuts);
                XrplAmount adjustedIn;
                XrplAmount adjustedOut;
                XrplAmount lastOut;

                if (stepAmount.In <= remainingIn)
                {
                    insAdjusted.Add(stepAmount.In);
                    outsAdjusted.Add(stepAmount.Out);
                    lastOut = stepAmount.Out;
                    adjustedIn = StepMath.Sum(insAdjusted, _in, rules);
                    adjustedOut = StepMath.Sum(outsAdjusted, _out, rules);
                    processMore = true;
                }
                else
                {
                    LimitStepIn(offer, ref offerAdjusted, ref stepAdjusted, ref ownerGivesAdjusted, rateIn, rateOut, remainingIn, rules);
                    insAdjusted.Add(remainingIn);
                    outsAdjusted.Add(stepAdjusted.Out);
                    lastOut = stepAdjusted.Out;
                    adjustedOut = StepMath.Sum(outsAdjusted, _out, rules);
                    adjustedIn = @in;
                    processMore = false;
                }

                if (adjustedOut > reverse.Out && adjustedIn <= reverse.In)
                {
                    // More out than the reverse pass found on no more in: find the input the reverse
                    // pass's output needs, and when it is the input consumed here, settle on that output.
                    outsAdjusted.Remove(lastOut);
                    XrplAmount remainingOut = StepMath.Subtract(reverse.Out, StepMath.Sum(outsAdjusted, _out, rules), rules);
                    (XrplAmount In, XrplAmount Out) offerReverse = offerAmount;
                    (XrplAmount In, XrplAmount Out) stepReverse = stepAmount;
                    XrplAmount ownerGivesReverse = ownerGives;
                    LimitStepOut(offer, ref offerReverse, ref stepReverse, ref ownerGivesReverse, rateIn, rateOut, remainingOut, rules);

                    if (stepReverse.In == remainingIn)
                    {
                        adjustedIn = @in;
                        adjustedOut = reverse.Out;
                        insAdjusted.Clear();
                        insAdjusted.Add(adjustedIn);
                        outsAdjusted.Clear();
                        outsAdjusted.Add(adjustedOut);

                        offerAdjusted = offerReverse;
                        stepAdjusted = (remainingIn, remainingOut);
                        ownerGivesAdjusted = ownerGivesReverse;
                    }
                    else
                    {
                        outsAdjusted.Add(lastOut);
                    }
                }

                savedIns.Clear();
                savedIns.AddRange(insAdjusted);
                savedOuts.Clear();
                savedOuts.AddRange(outsAdjusted);
                resultIn = adjustedIn;
                resultOut = adjustedOut;
                remainingIn = StepMath.Subtract(@in, resultIn, rules);
                ConsumeOffer(sb, offer, offerAdjusted, ownerGivesAdjusted);

                return processMore || offer.FullyConsumed;
            }

            DebtDirection previous = _previous?.DebtDirection(sb, StrandDirection.Forward) ?? Amounts.DebtDirection.Issues;
            (HashSet<string> toRemove, int consumed) = ForEachOffer(sb, afView, previous, EachOffer);
            _offersUsed = consumed;
            offersToRemove.UnionWith(toRemove);
            if (consumed >= MaxOffersToConsume)
                _inactive = true;

            if (remainingIn.IsNegative)
            {
                _cache = (XrplAmount.Zero(_in), XrplAmount.Zero(_out));
                return (StepMath.Typed(_cache.Value.In), StepMath.Typed(_cache.Value.Out));
            }

            if (remainingIn.IsZero)
                resultIn = @in;

            _cache = (resultIn, resultOut);
            return (StepMath.Typed(resultIn), StepMath.Typed(resultOut));
        }

        /// <summary><c>BookStep::check</c>: a book that changes the asset, outputs it once, has issuers, and is reached through a line that ripples.</summary>
        internal string Check(StrandContext context)
        {
            DexView view = context.View;
            AssetKey inKey = AssetKey.Of(_in);
            AssetKey outKey = AssetKey.Of(_out);
            if (inKey == outKey)
                return "temBAD_PATH";

            // Two books may not output the same asset: offers on one could unfund offers on the other.
            if (!context.SeenBookOuts.Add(outKey) || context.SeenDirectAssets[0].Contains(outKey))
                return "temBAD_PATH_LOOP";
            if (context.SeenDirectAssets[1].Contains(outKey))
                return "temBAD_PATH_LOOP";

            if ((inKey.Issuer.Length != 0 && !view.World.AccountExists(inKey.Issuer)) ||
                (outKey.Issuer.Length != 0 && !view.World.AccountExists(outKey.Issuer)))
            {
                return "tecNO_ISSUER";
            }

            if (context.PreviousStep?.DirectStepSourceAccount is { } previous && inKey.Issuer.Length != 0)
            {
                if (!view.LineExists(previous, inKey.Issuer, inKey.Currency))
                    return "terNO_LINE";
                if (view.LineInfo(previous, inKey.Issuer, inKey.Currency)?.Side(inKey.Issuer).NoRipple == true)
                    return "terNO_RIPPLE";
            }

            return null;
        }

        /// <summary>
        /// rippled's <c>FlowOfferStream</c> over <c>BookTip</c>: the book's live offers in order,
        /// each tip deleted as the walk moves past it, and the unusable ones - expired, empty,
        /// deep-frozen, out of their domain, unfunded, or too small to keep their quality -
        /// skipped on the way.
        /// </summary>
        private sealed class OfferStream
        {
            private readonly DexView _view;
            private readonly DexView _cancelView;
            private readonly IReadOnlyList<DexOffer> _book;
            private readonly int _limit;
            private readonly string _domain;
            private readonly bool _partial;
            private int _position;
            private DexOffer _tipEntry;

            internal OfferStream(DexView view, DexView cancelView, IReadOnlyList<DexOffer> book, int limit, string domain, bool partial)
            {
                _view = view;
                _cancelView = cancelView;
                _book = book;
                _limit = limit;
                _domain = domain;
                _partial = partial;
            }

            internal ClobOffer Tip { get; private set; }

            internal XrplAmount? OwnerFunds { get; private set; }

            internal HashSet<string> ToRemove { get; } = new HashSet<string>(StringComparer.Ordinal);

            internal int Count { get; private set; }

            internal void PermanentlyRemove(string index) => ToRemove.Add(index);

            /// <summary><c>TOfferStreamBase::step</c>, its checks in the protocol's order.</summary>
            internal bool Step()
            {
                for (;;)
                {
                    OwnerFunds = null;
                    Tip = null;

                    // BookTip::step deletes the previous tip before moving to the next one.
                    if (_tipEntry != null)
                    {
                        _view.DeleteOffer(_tipEntry.Index);
                        _tipEntry = null;
                    }

                    DexOffer entry = NextLive();
                    if (entry == null)
                    {
                        // The walk ran off what the snapshot read of the book, not off the book.
                        if (_partial)
                            _view.World.ReachedPartialBook = true;
                        return false;
                    }

                    _tipEntry = entry;
                    if (Count >= _limit)
                        return false;
                    Count++;

                    if (entry.Expiration is { } expiration && expiration <= _view.ParentCloseTime)
                    {
                        PermanentlyRemove(entry.Index);
                        continue;
                    }

                    ClobOffer offer = new ClobOffer(entry, _view.Offer(entry.Index));
                    if (StepMath.IsNotPositive(offer.In) || StepMath.IsNotPositive(offer.Out))
                    {
                        PermanentlyRemove(entry.Index);
                        continue;
                    }

                    if (_view.IsDeepFrozen(offer.Owner, offer.AssetIn))
                    {
                        PermanentlyRemove(entry.Index);
                        continue;
                    }

                    // A domain offer whose owner left the domain is removed where the domain is
                    // walked; before fixCleanup3_3_0, in the open book too.
                    if ((!_view.Rules.FixCleanup3_3_0 || _domain.Length != 0) && !string.IsNullOrEmpty(entry.DomainId) &&
                        !_view.World.AccountInDomain(entry.Account, entry.DomainId))
                    {
                        PermanentlyRemove(entry.Index);
                        continue;
                    }

                    XrplAmount funds = FundsOf(_view, offer);
                    OwnerFunds = funds;
                    if (StepMath.IsNotPositive(funds))
                    {
                        // Found unfunded, not made unfunded by this strand: removed for good.
                        if (FundsOf(_cancelView, offer) == funds)
                            PermanentlyRemove(entry.Index);
                        continue;
                    }

                    if (ShouldRemoveSmallIncreasedQualityOffer(offer, funds))
                    {
                        if (FundsOf(_cancelView, offer) == funds)
                            PermanentlyRemove(entry.Index);
                        continue;
                    }

                    Tip = offer;
                    return true;
                }
            }

            private DexOffer NextLive()
            {
                while (_position < _book.Count)
                {
                    DexOffer candidate = _book[_position];
                    if (!_view.Offer(candidate.Index).Deleted)
                        return candidate;
                    _position++;
                }

                return null;
            }

            /// <summary><c>accountFundsHelper</c>: an issued currency's issuer has what the offer gives.</summary>
            private static XrplAmount FundsOf(DexView view, ClobOffer offer)
            {
                if (offer.Out.Kind == AmountKind.Iou && string.Equals(offer.Owner, offer.AssetOut.Issuer, StringComparison.Ordinal))
                    return offer.Out;

                return view.AccountHolds(offer.Owner, offer.AssetOut);
            }

            /// <summary>
            /// <c>shouldRmSmallIncreasedQOffer</c>: an offer whose funded part is so small that
            /// rounding would make it better than its page is removed rather than left to block
            /// the book.
            /// </summary>
            private bool ShouldRemoveSmallIncreasedQualityOffer(ClobOffer offer, XrplAmount ownerFunds)
            {
                bool inIntegral = offer.In.IsIntegral;
                bool outIntegral = offer.Out.IsIntegral;
                if (!inIntegral && outIntegral)
                    return false;

                if (!inIntegral && !outIntegral && offer.In.Value >= offer.Out.Value)
                    return false;

                LedgerRules rules = _view.Rules;
                bool issuerHasUnlimitedFunds = !outIntegral &&
                                               string.Equals(offer.Owner, offer.AssetOut.Issuer, StringComparison.Ordinal);
                (XrplAmount In, XrplAmount Out) effective = (offer.In, offer.Out);
                if (!issuerHasUnlimitedFunds && ownerFunds < offer.Out)
                    effective = offer.Quality.CeilOutStrict(offer.In, offer.Out, ownerFunds, roundUp: false, rules);

                if (StepMath.IsNotPositive(effective.In) || StepMath.IsNotPositive(effective.Out))
                    return true;

                XrplAmount minPositive = inIntegral
                    ? XrplAmount.FromUnits(offer.AssetIn, offer.In.Kind, 1)
                    : XrplAmount.Canonical(offer.AssetIn, AmountKind.Iou, false, XrplAmount.MinIouMantissa, XrplAmount.MinIouExponent, NumberRounding.ToNearest);
                if (effective.In > minPositive)
                    return false;

                return XrplQuality.FromAmounts(effective.In, effective.Out, rules) < offer.Quality;
            }
        }
    }
}
