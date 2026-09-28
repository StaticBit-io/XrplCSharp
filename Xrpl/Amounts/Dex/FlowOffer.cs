using System;

using Xrpl.Sugar;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>An offer a book step can take: an order book offer or an AMM pool's synthetic one.</summary>
    internal abstract class FlowOffer
    {
        /// <summary>What the offer takes now.</summary>
        internal XrplAmount In { get; set; }

        /// <summary>What the offer gives now.</summary>
        internal XrplAmount Out { get; set; }

        internal abstract XrplQuality Quality { get; }

        internal abstract string Owner { get; }

        /// <summary>The offer's ledger index; null for an AMM offer.</summary>
        internal abstract string Key { get; }

        internal abstract bool IsAmm { get; }

        /// <summary>Whether the owner can pay without limit: it issues what it gives, or it is a pool.</summary>
        internal abstract bool IsFunded { get; }

        internal abstract bool FullyConsumed { get; }

        internal abstract (uint In, uint Out) AdjustRates(uint inRate, uint outRate);

        internal abstract (XrplAmount In, XrplAmount Out) LimitOut((XrplAmount In, XrplAmount Out) amount, XrplAmount limit, bool roundUp, LedgerRules rules);

        internal abstract (XrplAmount In, XrplAmount Out) LimitIn((XrplAmount In, XrplAmount Out) amount, XrplAmount limit, bool roundUp, LedgerRules rules);

        internal abstract void Consume(DexView view, XrplAmount consumedIn, XrplAmount consumedOut);

        internal abstract bool CheckInvariant(XrplAmount consumedIn, XrplAmount consumedOut, LedgerRules rules);
    }

    /// <summary>rippled's <c>TOffer</c>: an order book offer as the view holds it.</summary>
    internal sealed class ClobOffer : FlowOffer
    {
        private readonly DexOffer _offer;

        internal ClobOffer(DexOffer offer, OfferState state)
        {
            _offer = offer;
            In = state.TakerPays;
            Out = state.TakerGets;
        }

        internal DexOffer Source => _offer;

        internal override XrplQuality Quality => _offer.Quality;

        internal override string Owner => _offer.Account;

        internal override string Key => _offer.Index;

        internal override bool IsAmm => false;

        internal IssuedCurrency AssetIn => _offer.TakerPays.Asset;

        internal IssuedCurrency AssetOut => _offer.TakerGets.Asset;

        internal override bool IsFunded =>
            Out.Kind == AmountKind.Iou && string.Equals(_offer.Account, AssetOut.Issuer, StringComparison.Ordinal);

        internal override bool FullyConsumed => StepMath.IsNotPositive(In) || StepMath.IsNotPositive(Out);

        internal override (uint In, uint Out) AdjustRates(uint inRate, uint outRate) => (inRate, outRate);

        internal override (XrplAmount In, XrplAmount Out) LimitOut((XrplAmount In, XrplAmount Out) amount, XrplAmount limit, bool roundUp, LedgerRules rules) =>
            OfferCrossing.LimitOut(Quality, amount.In, amount.Out, limit, roundUp, rules);

        internal override (XrplAmount In, XrplAmount Out) LimitIn((XrplAmount In, XrplAmount Out) amount, XrplAmount limit, bool roundUp, LedgerRules rules) =>
            OfferCrossing.LimitIn(Quality, amount.In, amount.Out, limit, roundUp, rules);

        internal override void Consume(DexView view, XrplAmount consumedIn, XrplAmount consumedOut)
        {
            if (consumedIn > In)
                throw new InvalidOperationException("can't consume more than is available.");
            if (consumedOut > Out)
                throw new InvalidOperationException("can't produce more than is available.");

            In = StepMath.Subtract(In, consumedIn, view.Rules);
            Out = StepMath.Subtract(Out, consumedOut, view.Rules);
            view.ConsumeOffer(_offer.Index, consumedIn, consumedOut);
        }

        internal override bool CheckInvariant(XrplAmount consumedIn, XrplAmount consumedOut, LedgerRules rules)
        {
            if (!(rules ?? new LedgerRules()).CurrentFixAMMv1_3)
                return true;

            return !(consumedIn > In) && !(consumedOut > Out);
        }
    }
}
