using System;

using Xrpl.Sugar;

namespace Xrpl.Amounts
{
    /// <summary>
    /// The arithmetic of an AMM pool's synthetic offer, as rippled's payment engine does it: the
    /// swap formulas and the offer a pool puts beside an order book, with rippled's <c>Number</c>
    /// rounding.
    /// </summary>
    /// <remarks>
    /// The amounts are the pool's balances and what moves in or out of it, each in its own asset;
    /// the trading fee is in units of 1/100,000 (1000 is 1%). The rules decide the rounding:
    /// <c>fixAMMv1_1</c> for the swaps and the spot-price change, <c>fixAMMv1_2</c> for the
    /// largest offer beside a book, the <c>Number</c> scale for all of them. The results are the
    /// ones inside a transaction.
    /// </remarks>
    public static class AmmOfferMath
    {
        /// <summary><c>swapAssetIn</c>: what the pool pays out for <paramref name="amountIn"/>.</summary>
        /// <param name="poolIn">The pool's balance of the asset paid in.</param>
        /// <param name="poolOut">The pool's balance of the asset paid out.</param>
        /// <param name="amountIn">What goes into the pool.</param>
        /// <param name="tradingFee">The pool's trading fee, in units of 1/100,000.</param>
        /// <param name="rules">The amendments in force; all of them when null.</param>
        public static XrplAmount SwapIn(XrplAmount poolIn, XrplAmount poolOut, XrplAmount amountIn, ushort tradingFee, LedgerRules rules = null) =>
            AmmSwap.SwapAssetIn(poolIn, poolOut, amountIn, tradingFee, rules);

        /// <summary><c>swapAssetOut</c>: what the pool takes in to pay out <paramref name="amountOut"/>.</summary>
        /// <param name="poolIn">The pool's balance of the asset paid in.</param>
        /// <param name="poolOut">The pool's balance of the asset paid out.</param>
        /// <param name="amountOut">What comes out of the pool; less than <paramref name="poolOut"/>.</param>
        /// <param name="tradingFee">The pool's trading fee, in units of 1/100,000.</param>
        /// <param name="rules">The amendments in force; all of them when null.</param>
        public static XrplAmount SwapOut(XrplAmount poolIn, XrplAmount poolOut, XrplAmount amountOut, ushort tradingFee, LedgerRules rules = null) =>
            AmmSwap.SwapAssetOut(poolIn, poolOut, amountOut, tradingFee, rules);

        /// <summary>
        /// <c>changeSpotPriceQuality</c>: the offer that brings the pool's spot price to
        /// <paramref name="quality"/>, or that has that quality itself; null when neither can be generated.
        /// </summary>
        /// <param name="poolIn">The pool's balance of the asset paid in.</param>
        /// <param name="poolOut">The pool's balance of the asset paid out.</param>
        /// <param name="quality">The quality to reach, usually the best one of the order book beside the pool.</param>
        /// <param name="tradingFee">The pool's trading fee, in units of 1/100,000.</param>
        /// <param name="rules">The amendments in force; all of them when null.</param>
        public static (XrplAmount In, XrplAmount Out)? ChangeSpotPriceQuality(
            XrplAmount poolIn,
            XrplAmount poolOut,
            XrplQuality quality,
            ushort tradingFee,
            LedgerRules rules = null) =>
            AmmSwap.ChangeSpotPriceQuality(poolIn, poolOut, quality, tradingFee, rules);

        /// <summary>
        /// The offer the pool puts beside an order book whose best quality is
        /// <paramref name="bookQuality"/>, on a payment or a crossing along one strand
        /// (<c>AMMLiquidity::getOffer</c>): the offer that brings the pool's spot price to the
        /// book's quality - or, under <c>fixAMMv1_2</c>, the pool's largest offer when that one is
        /// better than the book - and the largest offer, 99% of <paramref name="poolOut"/>, when
        /// there is no book.
        /// </summary>
        /// <remarks>
        /// On several strands the engine sizes the pool's offers differently, in slices of the
        /// initial pool; that and its limit of 30 pool offers per transaction depend on the whole
        /// flow and are not modelled here.
        /// </remarks>
        /// <param name="poolIn">The pool's balance of the asset paid in.</param>
        /// <param name="poolOut">The pool's balance of the asset paid out.</param>
        /// <param name="tradingFee">The pool's trading fee, in units of 1/100,000.</param>
        /// <param name="bookQuality">The best quality of the order book beside the pool; null for an empty book.</param>
        /// <param name="rules">The amendments in force; all of them when null.</param>
        /// <returns>The offer, or null when the pool's spot price does not beat the book or no offer can be generated.</returns>
        public static AmmPoolOffer? PoolOffer(
            XrplAmount poolIn,
            XrplAmount poolOut,
            ushort tradingFee,
            XrplQuality? bookQuality,
            LedgerRules rules = null)
        {
            if (poolIn.Asset == null || poolOut.Asset == null)
                throw new ArgumentException("The pool's balances need their assets.");

            return AmmBookLiquidity.OfferFor((poolIn, poolOut), tradingFee, bookQuality, rules ?? new LedgerRules(), null) is { } offer
                ? new AmmPoolOffer(offer.In, offer.Out, offer.Quality)
                : null;
        }
    }

    /// <summary>The synthetic offer an AMM pool puts beside an order book.</summary>
    /// <param name="In">What the pool takes in.</param>
    /// <param name="Out">What the pool pays out.</param>
    /// <param name="Quality">
    /// The quality the offer ranks at: its own, or the pool's spot price for the largest offer
    /// when there is no book, as the engine ranks it.
    /// </param>
    public readonly record struct AmmPoolOffer(XrplAmount In, XrplAmount Out, XrplQuality Quality);
}
