using System;
using System.Collections.Generic;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>Whether a step's source owes its counterparty (redeems) or is owed (issues).</summary>
    internal enum DebtDirection
    {
        Issues,
        Redeems,
    }

    internal enum StrandDirection
    {
        Forward,
        Reverse,
    }

    /// <summary>rippled's <c>FlowException</c>: the strand fails, the transaction goes on.</summary>
    internal sealed class FlowFailedException : Exception
    {
        internal FlowFailedException(string result, string message)
            : base(message)
        {
            Result = result;
        }

        internal string Result { get; }
    }

    /// <summary>An asset as a strand's loop checks see it: a currency and its issuer, XRP with an empty one.</summary>
    internal readonly record struct AssetKey(string Currency, string Issuer)
    {
        internal static readonly AssetKey Xrp = new AssetKey("XRP", string.Empty);

        internal static AssetKey Of(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp ? Xrp : new AssetKey(asset.Currency, asset.Issuer);
    }

    /// <summary>rippled's <c>StrandContext</c>: what a step sees of the strand while it is built.</summary>
    internal sealed class StrandContext
    {
        internal DexView View { get; init; }

        internal string StrandSource { get; init; }

        internal string StrandDestination { get; init; }

        internal IssuedCurrency StrandDeliver { get; init; }

        internal XrplQuality? LimitQuality { get; init; }

        internal bool IsFirst { get; init; }

        internal bool IsLast { get; init; }

        /// <summary>Offer crossing, where the offer's owner pays the transfer fee; a payment otherwise.</summary>
        internal bool OfferCrossing { get; init; }

        internal bool IsDefaultPath { get; init; }

        internal FlowStep PreviousStep { get; init; }

        internal int StrandSize { get; init; }

        /// <summary>The assets direct steps have sent from [0] and to [1].</summary>
        internal HashSet<AssetKey>[] SeenDirectAssets { get; init; }

        internal HashSet<AssetKey> SeenBookOuts { get; init; }

        internal AmmFlowContext AmmContext { get; init; }

        /// <summary>The permissioned domain whose books the strand walks; null for the open books.</summary>
        internal string DomainId { get; init; }
    }

    /// <summary>rippled's <c>Step</c>: one hop of a strand, run backwards from the output, then forwards.</summary>
    internal abstract class FlowStep
    {
        /// <summary>The step's position in its strand.</summary>
        internal int Position { get; set; }

        internal abstract (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, OfferRemovals offersToRemove, XrplAmount @out);

        internal abstract (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, OfferRemovals offersToRemove, XrplAmount @in);

        internal abstract XrplAmount? CachedIn { get; }

        internal abstract XrplAmount? CachedOut { get; }

        internal virtual DebtDirection DebtDirection(DexView sb, StrandDirection direction) => Amounts.DebtDirection.Issues;

        internal virtual (XrplQuality? Quality, DebtDirection Direction) QualityUpperBound(DexView view, DebtDirection previous) =>
            (new XrplQuality(QualityOneEncoded), DebtDirection(view, StrandDirection.Forward));

        internal virtual (QualityFunction Function, DebtDirection Direction) GetQualityFunc(DexView view, DebtDirection previous)
        {
            (XrplQuality? quality, DebtDirection direction) = QualityUpperBound(view, previous);
            return quality is { } q
                ? (QualityFunction.ClobLike(q, view.Rules.Context), direction)
                : (null, direction);
        }

        /// <summary><c>lineQualityIn</c>: the destination's quality in on a direct step; parity otherwise.</summary>
        internal virtual uint LineQualityIn(DexView view) => OfferCrossing.QualityOne;

        internal virtual int OffersUsed => 0;

        internal virtual bool Inactive => false;

        /// <summary>The source of a direct step; null for any other step.</summary>
        internal virtual string DirectStepSourceAccount => null;

        /// <summary>A direct or XRP step's two accounts, the empty account standing for XRP; null for a book.</summary>
        internal virtual (string Source, string Destination)? DirectStepAccounts => null;

        /// <summary>A book step's assets; null for any other step.</summary>
        internal virtual (IssuedCurrency In, IssuedCurrency Out)? BookStepBook => null;

        internal bool IsBookStep => BookStepBook != null;

        /// <summary>The encoding of a quality of exactly 1 (<c>STAmount::kURateOne</c>).</summary>
        internal static readonly ulong QualityOneEncoded = (ulong)(-15 + 100) << 56 | 1_000_000_000_000_000UL;

        /// <summary><c>Quality(getRate(out, in))</c> of two rates given in billionths.</summary>
        internal static XrplQuality QualityOfRates(uint rateIn, uint rateOut, LedgerRules rules)
        {
            IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
            return XrplQuality.FromAmounts(
                XrplAmount.FromUnits(xrp, AmountKind.Xrp, rateIn),
                XrplAmount.FromUnits(xrp, AmountKind.Xrp, rateOut),
                rules);
        }

        /// <summary><c>checkFreeze</c>: a destination frozen globally, or its side of the line frozen.</summary>
        internal static string CheckFreeze(DexView view, string source, string destination, string currency)
        {
            if (view.World.Accounts.TryGetValue(destination, out DexAccount account) && account.GlobalFreeze)
                return "terNO_LINE";

            if (currency == null || currency == "XRP")
                return null;

            LineInfo line = view.LineInfo(source, destination, currency);
            if (line != null && (line.Side(destination).Freeze || line.DeepFrozen))
                return "terNO_LINE";

            return null;
        }

        /// <summary><c>checkNoRipple</c>: an account in the middle of a strand that set NoRipple on both lines.</summary>
        internal static string CheckNoRipple(DexView view, string previous, string current, string next, string currency)
        {
            if (!view.LineExists(previous, current, currency) || !view.LineExists(current, next, currency))
                return "terNO_LINE";

            LineInfo into = view.LineInfo(previous, current, currency);
            LineInfo outOf = view.LineInfo(current, next, currency);
            if (into != null && outOf != null && into.Side(current).NoRipple && outOf.Side(current).NoRipple)
                return "terNO_RIPPLE";

            return null;
        }
    }

    /// <summary>
    /// rippled's <c>DirectStepI</c>: an issued currency moving along the trust line between two
    /// accounts. A payment reads the line's qualities, limits and the source's debt direction,
    /// which decides who pays the issuer's transfer fee; offer crossing ignores the qualities and,
    /// at the end of the strand, the limit.
    /// </summary>
    internal sealed class DirectStep : FlowStep
    {
        private readonly string _source;
        private readonly string _destination;
        private readonly string _currency;
        private readonly IssuedCurrency _asset;
        private readonly FlowStep _previous;
        private readonly bool _isLast;
        private readonly bool _offerCrossing;
        private (XrplAmount In, XrplAmount SrcToDst, XrplAmount Out, DebtDirection SrcDebtDir)? _cache;

        internal DirectStep(StrandContext context, string source, string destination, string currency)
        {
            _source = source;
            _destination = destination;
            _currency = currency;
            _asset = StepMath.TypedIou(currency);
            _previous = context.PreviousStep;
            _isLast = context.IsLast;
            _offerCrossing = context.OfferCrossing;
        }

        internal override XrplAmount? CachedIn => _cache?.In;

        internal override XrplAmount? CachedOut => _cache?.Out;

        internal string Currency => _currency;

        internal override string DirectStepSourceAccount => _source;

        internal override (string Source, string Destination)? DirectStepAccounts => (_source, _destination);

        /// <summary><c>quality</c>: the destination's quality in or the source's quality out; parity when offer crossing.</summary>
        private uint Quality(DexView view, bool qualityIn)
        {
            if (_offerCrossing || string.Equals(_source, _destination, StringComparison.Ordinal))
                return OfferCrossing.QualityOne;

            LineInfo line = view.LineInfo(_source, _destination, _currency);
            if (line == null)
                return OfferCrossing.QualityOne;

            uint quality = qualityIn ? line.Side(_destination).QualityIn : line.Side(_source).QualityOut;
            return quality == 0 ? OfferCrossing.QualityOne : quality;
        }

        internal override uint LineQualityIn(DexView view) => Quality(view, qualityIn: true);

        /// <summary><c>accountHolds(src, currency, dst, IgnoreFreeze)</c>: what the source holds of the destination's currency.</summary>
        private XrplAmount SourceOwed(DexView sb) =>
            sb.Spendable(new HoldingKey(_source, _currency, _destination)).WithAsset(_asset);

        /// <summary><c>maxPaymentFlow</c>: what the source holds, or failing that, what the destination trusts it for.</summary>
        private (XrplAmount Max, DebtDirection Direction) MaxPaymentFlow(DexView sb)
        {
            XrplAmount owed = SourceOwed(sb);
            if (StepMath.IsPositive(owed))
                return (owed, Amounts.DebtDirection.Redeems);

            XrplAmount? limit = sb.LineInfo(_source, _destination, _currency)?.Side(_destination).Limit;
            XrplAmount trusted = limit is { } l ? l.WithAsset(_asset) : XrplAmount.Zero(_asset);
            return (StepMath.Add(trusted, owed, sb.Rules), Amounts.DebtDirection.Issues);
        }

        /// <summary><c>maxFlow</c>: offer crossing leaves the last step unlimited.</summary>
        private (XrplAmount Max, DebtDirection Direction) MaxFlow(DexView sb, XrplAmount desired) =>
            _offerCrossing && _isLast ? (desired, Amounts.DebtDirection.Issues) : MaxPaymentFlow(sb);

        internal override DebtDirection DebtDirection(DexView sb, StrandDirection direction)
        {
            if (direction == StrandDirection.Forward && _cache != null)
                return _cache.Value.SrcDebtDir;

            return StepMath.IsPositive(SourceOwed(sb)) ? Amounts.DebtDirection.Redeems : Amounts.DebtDirection.Issues;
        }

        /// <summary><c>qualitiesSrcRedeems</c>: the previous step's quality in carries over when larger.</summary>
        private (uint SrcQOut, uint DstQIn) QualitiesSourceRedeems(DexView view)
        {
            if (_previous == null)
                return (OfferCrossing.QualityOne, OfferCrossing.QualityOne);

            uint previousQIn = _previous.LineQualityIn(view);
            uint srcQOut = Quality(view, qualityIn: false);
            return (Math.Max(previousQIn, srcQOut), OfferCrossing.QualityOne);
        }

        /// <summary><c>qualitiesSrcIssues</c>: issuing after a step that redeemed charges the source's transfer fee.</summary>
        private (uint SrcQOut, uint DstQIn) QualitiesSourceIssues(DexView view, DebtDirection previous)
        {
            uint srcQOut = previous == Amounts.DebtDirection.Redeems ? view.World.TransferRate(_source) : OfferCrossing.QualityOne;
            uint dstQIn = Quality(view, qualityIn: true);
            if (_isLast && dstQIn > OfferCrossing.QualityOne)
                dstQIn = OfferCrossing.QualityOne;

            return (srcQOut, dstQIn);
        }

        private (uint SrcQOut, uint DstQIn) Qualities(DexView sb, DebtDirection sourceDirection, StrandDirection strandDirection)
        {
            if (sourceDirection == Amounts.DebtDirection.Redeems)
                return QualitiesSourceRedeems(sb);

            DebtDirection previous = _previous?.DebtDirection(sb, strandDirection) ?? Amounts.DebtDirection.Issues;
            return QualitiesSourceIssues(sb, previous);
        }

        internal override (XrplQuality? Quality, DebtDirection Direction) QualityUpperBound(DexView view, DebtDirection previous)
        {
            DebtDirection direction = DebtDirection(view, StrandDirection.Forward);
            (uint srcQOut, uint dstQIn) = direction == Amounts.DebtDirection.Redeems
                ? QualitiesSourceRedeems(view)
                : QualitiesSourceIssues(view, previous);
            return (QualityOfRates(srcQOut, dstQIn, view.Rules), direction);
        }

        internal override (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, OfferRemovals offersToRemove, XrplAmount @out)
        {
            @out = @out.WithAsset(_asset);
            LedgerRules rules = sb.Rules;
            _cache = null;
            (XrplAmount max, DebtDirection direction) = MaxFlow(sb, @out);
            (uint srcQOut, uint dstQIn) = Qualities(sb, direction, StrandDirection.Reverse);
            if (StepMath.IsNotPositive(max))
            {
                XrplAmount zero = XrplAmount.Zero(_asset);
                _cache = (zero, zero, zero, direction);
                return (zero, zero);
            }

            XrplAmount srcToDst = OfferCrossing.MulRatio(@out, OfferCrossing.QualityOne, dstQIn, roundUp: true, rules);
            if (srcToDst <= max)
            {
                XrplAmount @in = OfferCrossing.MulRatio(srcToDst, srcQOut, OfferCrossing.QualityOne, roundUp: true, rules);
                _cache = (@in, srcToDst, @out, direction);
                sb.Send(_source, _destination, srcToDst);
                return (@in, @out);
            }

            XrplAmount limitedIn = OfferCrossing.MulRatio(max, srcQOut, OfferCrossing.QualityOne, roundUp: true, rules);
            XrplAmount actualOut = OfferCrossing.MulRatio(max, dstQIn, OfferCrossing.QualityOne, roundUp: false, rules);
            _cache = (limitedIn, max, actualOut, direction);
            sb.Send(_source, _destination, max);
            return (limitedIn, actualOut);
        }

        internal override (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, OfferRemovals offersToRemove, XrplAmount @in)
        {
            @in = @in.WithAsset(_asset);
            LedgerRules rules = sb.Rules;
            (XrplAmount max, DebtDirection direction) = MaxFlow(sb, _cache.Value.SrcToDst);
            (uint srcQOut, uint dstQIn) = Qualities(sb, direction, StrandDirection.Forward);
            if (StepMath.IsNotPositive(max))
            {
                XrplAmount zero = XrplAmount.Zero(_asset);
                _cache = (zero, zero, zero, direction);
                return (zero, zero);
            }

            XrplAmount srcToDst = OfferCrossing.MulRatio(@in, OfferCrossing.QualityOne, srcQOut, roundUp: false, rules);
            if (srcToDst <= max)
            {
                XrplAmount @out = OfferCrossing.MulRatio(srcToDst, dstQIn, OfferCrossing.QualityOne, roundUp: false, rules);
                SetCacheLimiting(rules, @in, srcToDst, @out, direction);
            }
            else
            {
                XrplAmount actualIn = OfferCrossing.MulRatio(max, srcQOut, OfferCrossing.QualityOne, roundUp: true, rules);
                XrplAmount @out = OfferCrossing.MulRatio(max, dstQIn, OfferCrossing.QualityOne, roundUp: false, rules);
                SetCacheLimiting(rules, actualIn, max, @out, direction);
            }

            sb.Send(_source, _destination, _cache.Value.SrcToDst);
            return (_cache.Value.In, _cache.Value.Out);
        }

        /// <summary><c>setCacheLimiting</c>: the forward pass never delivers more than the reverse pass found.</summary>
        private void SetCacheLimiting(LedgerRules rules, XrplAmount fwdIn, XrplAmount fwdSrcToDst, XrplAmount fwdOut, DebtDirection direction)
        {
            var cache = _cache.Value;
            if (cache.In < fwdIn)
            {
                XrplAmount smallDiff = XrplAmount.Parse(_asset, "1e-9");
                XrplAmount diff = StepMath.Subtract(fwdIn, cache.In, rules);
                if (diff > smallDiff &&
                    (fwdIn.StExponent != cache.In.StExponent || cache.In.StMantissa == 0 ||
                     (double)fwdIn.StMantissa / cache.In.StMantissa > 1.01))
                {
                    _cache = (fwdIn, fwdSrcToDst, fwdOut, direction);
                    return;
                }
            }

            _cache = (
                fwdIn,
                fwdSrcToDst < cache.SrcToDst ? fwdSrcToDst : cache.SrcToDst,
                fwdOut < cache.Out ? fwdOut : cache.Out,
                direction);
        }

        /// <summary><c>DirectStepI::check</c> and the payment's or the crossing's own checks.</summary>
        internal string Check(StrandContext context)
        {
            DexView view = context.View;
            if (string.IsNullOrEmpty(_source) || string.IsNullOrEmpty(_destination))
                return "temBAD_PATH";
            if (string.Equals(_source, _destination, StringComparison.Ordinal))
                return "temBAD_PATH";
            if (!view.World.AccountExists(_source))
                return "terNO_ACCOUNT";

            // A pure issue or redeem cannot be frozen.
            if (!(context.IsLast && context.IsFirst) && CheckFreeze(view, _source, _destination, _currency) is { } frozen)
                return frozen;

            if (context.PreviousStep?.DirectStepSourceAccount is { } previousSource &&
                CheckNoRipple(view, previousSource, _source, _destination, _currency) is { } noRipple)
            {
                return noRipple;
            }

            AssetKey sourceIssue = new AssetKey(_currency, _source);
            AssetKey destinationIssue = new AssetKey(_currency, _destination);
            if (context.SeenBookOuts.Contains(sourceIssue))
            {
                if (context.PreviousStep == null)
                    return "temBAD_PATH_LOOP";
                if (context.PreviousStep.BookStepBook is { } book && AssetKey.Of(book.Out) != sourceIssue)
                    return "temBAD_PATH_LOOP";
            }

            if (!context.SeenDirectAssets[0].Add(sourceIssue) || !context.SeenDirectAssets[1].Add(destinationIssue))
                return "temBAD_PATH_LOOP";

            return _offerCrossing ? null : CheckPayment(context);
        }

        /// <summary><c>DirectIPaymentStep::check</c>: the line exists, is authorized, ripples, and has room.</summary>
        private string CheckPayment(StrandContext context)
        {
            DexView view = context.View;
            if (!view.LineExists(_source, _destination, _currency))
                return "terNO_LINE";

            LineInfo line = view.LineInfo(_source, _destination, _currency);
            XrplAmount balance = view.Read(new HoldingKey(_source, _currency, _destination)).Current;
            if (view.World.Accounts.TryGetValue(_source, out DexAccount source) && source.RequireAuth &&
                !(line?.Side(_source).Auth ?? false) && balance.IsZero)
            {
                return "terNO_AUTH";
            }

            if (context.PreviousStep != null && context.PreviousStep.IsBookStep && (line?.Side(_source).NoRipple ?? false))
                return "terNO_RIPPLE";

            // What the source holds of the destination's currency: when none, the destination
            // must still trust the source for more than it holds of the source's.
            XrplAmount owed = balance;
            if (StepMath.IsNotPositive(owed))
            {
                XrplAmount limit = line?.Side(_destination).Limit is { } l ? l.WithAsset(owed.Asset) : XrplAmount.Zero(owed.Asset);
                if (-owed >= limit)
                    return "tecPATH_DRY";
            }

            return null;
        }
    }

    /// <summary>
    /// rippled's <c>XRPEndpointStep</c>: XRP leaving the source at the start of a strand, capped
    /// by what it can spend, or reaching the destination at the end.
    /// </summary>
    internal sealed class XrpEndpointStep : FlowStep
    {
        private readonly string _account;
        private readonly bool _isLast;
        private readonly int _reserveReduction;
        private XrplAmount? _cache;

        /// <param name="context">The strand being built.</param>
        /// <param name="account">The source or the destination.</param>
        /// <param name="reserveReduction">
        /// -1 at the start of an offer-crossing strand when the taker has no trust line for what it
        /// buys, so the reserve the new line will take is not held back (<c>computeReserveReduction</c>).
        /// </param>
        internal XrpEndpointStep(StrandContext context, string account, int reserveReduction)
        {
            _account = account;
            _isLast = context.IsLast;
            _reserveReduction = reserveReduction;
        }

        internal override XrplAmount? CachedIn => _cache;

        internal override XrplAmount? CachedOut => _cache;

        internal override (string Source, string Destination)? DirectStepAccounts =>
            _isLast ? (string.Empty, _account) : (_account, string.Empty);

        internal override (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, OfferRemovals offersToRemove, XrplAmount @out)
        {
            XrplAmount balance = sb.XrpLiquid(_account, _reserveReduction);
            XrplAmount result = _isLast ? @out : StepMath.Min(balance, @out);
            Send(sb, result);
            _cache = result;
            return (result, result);
        }

        internal override (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, OfferRemovals offersToRemove, XrplAmount @in)
        {
            XrplAmount balance = sb.XrpLiquid(_account, _reserveReduction);
            XrplAmount result = _isLast ? @in : StepMath.Min(balance, @in);
            Send(sb, result);
            _cache = result;
            return (result, result);
        }

        private void Send(DexView sb, XrplAmount amount)
        {
            if (_isLast)
                sb.Send(string.Empty, _account, amount);
            else
                sb.Send(_account, string.Empty, amount);
        }

        /// <summary><c>XRPEndpointStep::check</c>.</summary>
        internal string Check(StrandContext context)
        {
            if (string.IsNullOrEmpty(_account))
                return "temBAD_PATH";
            if (!context.View.World.AccountExists(_account))
                return "terNO_ACCOUNT";
            if (!context.IsFirst && !context.IsLast)
                return "temBAD_PATH";

            string source = _isLast ? string.Empty : _account;
            string destination = _isLast ? _account : string.Empty;
            if (CheckFreeze(context.View, source, destination, null) is { } frozen)
                return frozen;

            if (!context.SeenDirectAssets[_isLast ? 0 : 1].Add(AssetKey.Xrp))
                return "temBAD_PATH_LOOP";

            return null;
        }
    }
}
