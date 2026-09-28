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

    /// <summary>rippled's <c>Step</c>: one hop of a strand, run backwards from the output, then forwards.</summary>
    internal abstract class FlowStep
    {
        internal abstract (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @out);

        internal abstract (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @in);

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

        internal virtual int OffersUsed => 0;

        internal virtual bool Inactive => false;

        /// <summary>The source of a direct step; null for any other step.</summary>
        internal virtual string DirectStepSourceAccount => null;

        internal virtual bool IsBookStep => false;

        /// <summary>The encoding of a quality of exactly 1 (<c>STAmount::kURateOne</c>).</summary>
        internal static readonly ulong QualityOneEncoded = (ulong)(-15 + 100) << 56 | 1_000_000_000_000_000UL;
    }

    /// <summary>
    /// rippled's <c>DirectIOfferCrossingStep</c>: an issued currency between the taker and its
    /// issuer. Offer crossing ignores the trust line's qualities, and at the end of the strand
    /// its limit, so the step passes amounts through, capped at the start of the strand by what
    /// the taker holds.
    /// </summary>
    internal sealed class DirectCrossingStep : FlowStep
    {
        private readonly string _source;
        private readonly string _destination;
        private readonly IssuedCurrency _asset;
        private readonly bool _isLast;
        private (XrplAmount In, XrplAmount SrcToDst, XrplAmount Out, DebtDirection SrcDebtDir)? _cache;

        /// <param name="source">The account sending.</param>
        /// <param name="destination">The account receiving.</param>
        /// <param name="asset">The currency, issued by whichever of the two is the issuer.</param>
        /// <param name="isLast">Whether this is the strand's last step.</param>
        internal DirectCrossingStep(string source, string destination, IssuedCurrency asset, bool isLast)
        {
            _source = source;
            _destination = destination;
            _asset = asset;
            _isLast = isLast;
        }

        internal override XrplAmount? CachedIn => _cache?.In;

        internal override XrplAmount? CachedOut => _cache?.Out;

        internal override string DirectStepSourceAccount => _source;

        /// <summary>The currency as issued by <paramref name="issuer"/>.</summary>
        private IssuedCurrency AssetIssuedBy(string issuer) => new IssuedCurrency { Currency = _asset.Currency, Issuer = issuer };

        internal override DebtDirection DebtDirection(DexView sb, StrandDirection direction)
        {
            if (direction == StrandDirection.Forward && _cache != null)
                return _cache.Value.SrcDebtDir;

            return StepMath.IsPositive(SourceOwed(sb)) ? Amounts.DebtDirection.Redeems : Amounts.DebtDirection.Issues;
        }

        /// <summary><c>accountHolds(src, currency, dst, IgnoreFreeze)</c>: what the source holds of the destination's currency.</summary>
        private XrplAmount SourceOwed(DexView sb)
        {
            IssuedCurrency issuedByDestination = AssetIssuedBy(_destination);
            HoldingKey key = HoldingKey.Of(_source, issuedByDestination);
            return sb.Read(key).Exists ? sb.Spendable(key) : XrplAmount.Zero(issuedByDestination);
        }

        /// <summary><c>maxFlow</c>: the last step is unlimited; otherwise what the source holds.</summary>
        private (XrplAmount Max, DebtDirection Direction) MaxFlow(DexView sb, XrplAmount desired)
        {
            if (_isLast)
                return (desired, Amounts.DebtDirection.Issues);

            XrplAmount owed = SourceOwed(sb);
            if (StepMath.IsPositive(owed))
                return (Retag(owed), Amounts.DebtDirection.Redeems);

            // The issuer extends the taker no credit in a snapshot, so the step is dry.
            return (Retag(owed), Amounts.DebtDirection.Issues);
        }

        private XrplAmount Retag(XrplAmount amount) => XrplAmount.FromNumber(_asset, amount.Value);

        internal override (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @out)
        {
            _cache = null;
            (XrplAmount max, DebtDirection direction) = MaxFlow(sb, @out);
            if (StepMath.IsNotPositive(max))
            {
                XrplAmount zero = XrplAmount.Zero(_asset);
                _cache = (zero, zero, zero, direction);
                return (zero, zero);
            }

            // Both qualities are QUALITY_ONE when offer crossing: amounts pass through unchanged.
            XrplAmount srcToDst = @out;
            if (srcToDst <= max)
            {
                _cache = (srcToDst, srcToDst, @out, direction);
                Send(sb, srcToDst);
                return (srcToDst, @out);
            }

            _cache = (max, max, max, direction);
            Send(sb, max);
            return (max, max);
        }

        internal override (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @in)
        {
            (XrplAmount max, DebtDirection direction) = MaxFlow(sb, _cache.Value.SrcToDst);
            if (StepMath.IsNotPositive(max))
            {
                XrplAmount zero = XrplAmount.Zero(_asset);
                _cache = (zero, zero, zero, direction);
                return (zero, zero);
            }

            XrplAmount srcToDst = @in;
            if (srcToDst <= max)
                SetCacheLimiting(sb.Rules, @in, srcToDst, srcToDst, direction);
            else
                SetCacheLimiting(sb.Rules, max, max, max, direction);

            Send(sb, _cache.Value.SrcToDst);
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

        private void Send(DexView sb, XrplAmount amount) => sb.Send(_source, _destination, amount);
    }

    /// <summary>
    /// rippled's <c>XRPEndpointOfferCrossingStep</c>: XRP leaving the taker at the start of a
    /// strand, capped by what it can spend, or reaching it at the end.
    /// </summary>
    internal sealed class XrpEndpointCrossingStep : FlowStep
    {
        private readonly string _account;
        private readonly bool _isLast;
        private readonly int _reserveReduction;
        private XrplAmount? _cache;

        /// <param name="account">The taker.</param>
        /// <param name="isLast">Whether the step ends the strand.</param>
        /// <param name="reserveReduction">
        /// -1 at the start of a strand when the taker has no trust line for what it buys, so the
        /// reserve the new line will take is not held back (<c>computeReserveReduction</c>).
        /// </param>
        internal XrpEndpointCrossingStep(string account, bool isLast, int reserveReduction)
        {
            _account = account;
            _isLast = isLast;
            _reserveReduction = reserveReduction;
        }

        internal override XrplAmount? CachedIn => _cache;

        internal override XrplAmount? CachedOut => _cache;

        internal override (XrplAmount In, XrplAmount Out) Rev(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @out)
        {
            XrplAmount balance = sb.XrpLiquid(_account, _reserveReduction);
            XrplAmount result = _isLast ? @out : StepMath.Min(balance, @out);
            Send(sb, result);
            _cache = result;
            return (result, result);
        }

        internal override (XrplAmount In, XrplAmount Out) Fwd(DexView sb, DexView afView, HashSet<string> offersToRemove, XrplAmount @in)
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
    }
}
