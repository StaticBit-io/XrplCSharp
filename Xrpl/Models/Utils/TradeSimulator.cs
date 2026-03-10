using System;
using System.Collections.Generic;
using System.Linq;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Utils;
using Offer = Xrpl.Models.Transactions.Offer;

namespace Xrpl.Models.Utils
{
    /// <summary>
    /// Represents a single fill step in a trade simulation, indicating
    /// which source provided liquidity and the amounts exchanged.
    /// </summary>
    public class FillStep
    {
        /// <summary>
        /// The liquidity source type.
        /// </summary>
        public enum SourceType
        {
            /// <summary>Filled from a DEX order book offer.</summary>
            OrderBook,
            /// <summary>Filled from an AMM pool.</summary>
            Amm,
        }

        /// <summary>The source of this fill (order book or AMM).</summary>
        public SourceType Source { get; set; }
        /// <summary>Amount spent (in taker_gets currency) for this step.</summary>
        public decimal AmountIn { get; set; }
        /// <summary>Amount received (in taker_pays currency) for this step.</summary>
        public decimal AmountOut { get; set; }
        /// <summary>Effective price of this fill step (taker_pays per taker_gets).</summary>
        public decimal Price { get; set; }
        /// <summary>Account address of the offer owner, or null for AMM fills.</summary>
        public string OfferAccount { get; set; }
    }

    /// <summary>
    /// Contains the full result of a simulated trade across the order book and AMM.
    /// </summary>
    public class TradeSimulationResult
    {
        /// <summary>Total amount received from the trade (in taker_pays currency).</summary>
        public decimal TotalReceived { get; set; }
        /// <summary>Total amount spent (in taker_gets currency).</summary>
        public decimal TotalSpent { get; set; }
        /// <summary>Amount received from order book fills.</summary>
        public decimal FromOrderBook { get; set; }
        /// <summary>Amount received from the AMM pool.</summary>
        public decimal FromAmm { get; set; }
        /// <summary>Total trading fee earned by the AMM pool.</summary>
        public decimal AmmPoolFee { get; set; }
        /// <summary>Effective average price of the entire trade (taker_pays per taker_gets).</summary>
        public decimal EffectivePrice { get; set; }
        /// <summary>Spot price before the trade (best available price).</summary>
        public decimal SpotPriceBefore { get; set; }
        /// <summary>Spot price after the trade.</summary>
        public decimal SpotPriceAfter { get; set; }
        /// <summary>Price impact as a percentage (0–100).</summary>
        public decimal PriceImpactPercent { get; set; }
        /// <summary>Updated order book after the trade (remaining offers).</summary>
        public List<Offer> RemainingOffers { get; set; }
        /// <summary>Updated AMM pool state after the trade (null if no AMM).</summary>
        public AMMInfo UpdatedAmm { get; set; }
        /// <summary>Details of each fill step in execution order.</summary>
        public List<FillStep> Steps { get; set; }
    }

    /// <summary>
    /// Simulates XRPL trade execution across the DEX order book and AMM,
    /// interleaving between both sources to always take the best available price.
    /// </summary>
    public static class TradeSimulator
    {
        /// <summary>
        /// Simulates selling a specified amount of taker_gets (Amount) for taker_pays (Amount2).
        /// Interleaves between the order book and AMM, always choosing the source
        /// that offers the best price (highest taker_pays per taker_gets) for the seller.
        /// </summary>
        /// <param name="orderBook">List of order book offers from book_offers response (can be null or empty).</param>
        /// <param name="amm">AMM pool info where Amount=taker_gets, Amount2=taker_pays (can be null).</param>
        /// <param name="amountToSpend">Amount of taker_gets (Amount) to sell.</param>
        /// <param name="isTakerGetsXrp">Whether taker_gets is XRP (reserved for future precision handling).</param>
        /// <returns>A <see cref="TradeSimulationResult"/> with full trade details.</returns>
        public static TradeSimulationResult SimulateTrade(
            List<Offer> orderBook,
            AMMInfo amm,
            decimal amountToSpend,
            bool isTakerGetsXrp)
        {
            var steps = new List<FillStep>();
            decimal totalSpent = 0;
            decimal totalReceived = 0;
            decimal fromOrderBook = 0;
            decimal fromAmm = 0;
            decimal ammPoolFee = 0;
            decimal remaining = amountToSpend;

            var filteredOffers = FilterAndSortOffers(orderBook);
            int offerIdx = 0;

            AMMInfo currentAmm = amm != null ? AmmInfoExtensions.ClonePool(amm) : null;

            decimal spotPriceBefore = ComputeSpotPrice(filteredOffers, offerIdx, currentAmm);

            while (remaining > 0 && (offerIdx < filteredOffers.Count || currentAmm != null))
            {
                decimal? bookQuality = offerIdx < filteredOffers.Count
                    ? filteredOffers[offerIdx].Quality
                    : null;

                decimal? ammPrice = currentAmm != null && currentAmm.Amount.ValueAsNumber > 0
                    ? currentAmm.GetEffectiveAmmPrice()
                    : null;

                bool useAmm;
                if (bookQuality.HasValue && ammPrice.HasValue)
                    useAmm = ammPrice.Value >= bookQuality.Value;
                else if (ammPrice.HasValue)
                    useAmm = true;
                else if (bookQuality.HasValue)
                    useAmm = false;
                else
                    break;

                if (useAmm)
                {
                    decimal deltaIn;
                    if (offerIdx < filteredOffers.Count && filteredOffers[offerIdx].Quality.HasValue)
                    {
                        decimal qNext = filteredOffers[offerIdx].Quality.Value;
                        deltaIn = ComputeAmmDeltaToPrice(currentAmm, qNext);
                        if (deltaIn <= 0)
                        {
                            if (!bookQuality.HasValue)
                                break;
                            useAmm = false;
                        }
                        else
                        {
                            if (deltaIn > remaining)
                                deltaIn = remaining;
                        }
                    }
                    else
                    {
                        deltaIn = remaining;
                    }

                    if (useAmm && deltaIn > 0)
                    {
                        var swapResult = currentAmm.SwapAmount1ForAmount2(deltaIn);
                        currentAmm = swapResult.UpdatedPool;

                        steps.Add(new FillStep
                        {
                            Source = FillStep.SourceType.Amm,
                            AmountIn = swapResult.AmountIn,
                            AmountOut = swapResult.AmountOut,
                            Price = swapResult.AmountIn != 0 ? swapResult.AmountOut / swapResult.AmountIn : 0,
                            OfferAccount = null,
                        });

                        totalSpent += swapResult.AmountIn;
                        totalReceived += swapResult.AmountOut;
                        fromAmm += swapResult.AmountOut;
                        ammPoolFee += swapResult.Fee;
                        remaining -= swapResult.AmountIn;
                        continue;
                    }
                }

                if (offerIdx < filteredOffers.Count)
                {
                    var offer = filteredOffers[offerIdx];
                    decimal availableTakerGets = GetAvailableTakerGets(offer);
                    decimal amountFilled = Math.Min(remaining, availableTakerGets);

                    if (amountFilled <= 0)
                    {
                        offerIdx++;
                        continue;
                    }

                    decimal quality = offer.Quality ?? 0;
                    decimal amountOut = amountFilled * quality;

                    steps.Add(new FillStep
                    {
                        Source = FillStep.SourceType.OrderBook,
                        AmountIn = amountFilled,
                        AmountOut = amountOut,
                        Price = quality,
                        OfferAccount = offer.Account,
                    });

                    totalSpent += amountFilled;
                    totalReceived += amountOut;
                    fromOrderBook += amountOut;
                    remaining -= amountFilled;

                    availableTakerGets -= amountFilled;
                    if (availableTakerGets <= 0)
                    {
                        offerIdx++;
                    }
                    else
                    {
                        offer.TakerGets.ValueAsNumber = offer.TakerGets.ValueAsNumber - amountFilled;
                        offer.TakerPays.ValueAsNumber = offer.TakerPays.ValueAsNumber - amountOut;
                        if (offer.TakerGetsFunded != null)
                            offer.TakerGetsFunded.ValueAsNumber = offer.TakerGetsFunded.ValueAsNumber - amountFilled;
                    }
                }
                else
                {
                    break;
                }
            }

            var remainingOffers = filteredOffers.Skip(offerIdx).ToList();
            decimal spotPriceAfter = ComputeSpotPrice(remainingOffers, 0, currentAmm);
            if (spotPriceAfter == 0 && steps.Count > 0)
                spotPriceAfter = steps.Last().Price;
            if (spotPriceBefore == 0 && steps.Count > 0)
                spotPriceBefore = steps.First().Price;

            decimal effectivePrice = totalSpent != 0 ? totalReceived / totalSpent : 0;
            decimal priceImpact = spotPriceBefore != 0
                ? ((spotPriceAfter - spotPriceBefore) / spotPriceBefore) * 100m
                : 0;

            return new TradeSimulationResult
            {
                TotalReceived = totalReceived,
                TotalSpent = totalSpent,
                FromOrderBook = fromOrderBook,
                FromAmm = fromAmm,
                AmmPoolFee = ammPoolFee,
                EffectivePrice = effectivePrice,
                SpotPriceBefore = spotPriceBefore,
                SpotPriceAfter = spotPriceAfter,
                PriceImpactPercent = priceImpact,
                RemainingOffers = remainingOffers,
                UpdatedAmm = currentAmm,
                Steps = steps,
            };
        }

        /// <summary>
        /// Filters out unfunded offers and sorts by quality ascending (best price first).
        /// </summary>
        private static List<Offer> FilterAndSortOffers(List<Offer> orderBook)
        {
            if (orderBook == null || orderBook.Count == 0)
                return new List<Offer>();

            return orderBook
                .Where(o => o.OwnerFunds != "0")
                .Where(o => !(o.TakerGetsFunded != null && o.TakerGetsFunded.ValueAsNumber == 0))
                .Where(o => o.Quality.HasValue && o.Quality.Value > 0)
                .OrderByDescending(o => o.Quality)
                .ToList();
        }

        /// <summary>
        /// Computes the best available spot price from the order book and AMM.
        /// </summary>
        private static decimal ComputeSpotPrice(List<Offer> offers, int offerIdx, AMMInfo amm)
        {
            decimal? bookPrice = offerIdx < offers.Count ? offers[offerIdx].Quality : null;
            decimal? ammPrice = amm != null && amm.Amount.ValueAsNumber > 0
                ? amm.GetEffectiveAmmPrice()
                : null;

            if (bookPrice.HasValue && ammPrice.HasValue)
                return Math.Max(bookPrice.Value, ammPrice.Value);
            if (bookPrice.HasValue)
                return bookPrice.Value;
            if (ammPrice.HasValue)
                return ammPrice.Value;
            return 0;
        }

        /// <summary>
        /// Determines how much taker_gets is available from an offer, considering funding.
        /// </summary>
        private static decimal GetAvailableTakerGets(Offer offer)
        {
            if (offer.TakerGetsFunded != null && offer.TakerGetsFunded.ValueAsNumber > 0)
                return offer.TakerGetsFunded.ValueAsNumber;

            decimal takerGets = offer.TakerGets.ValueAsNumber;
            if (!string.IsNullOrEmpty(offer.OwnerFunds) &&
                decimal.TryParse(offer.OwnerFunds, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out decimal ownerFunds))
            {
                return Math.Min(takerGets, ownerFunds);
            }

            return takerGets;
        }

        /// <summary>
        /// Computes the deltaIn needed to move the AMM price to match a target quality.
        /// Solves the quadratic: a*x^2 + b*x + c = 0 where
        /// a = Q*(1-f), b = Q*reserveIn*(2-f), c = Q*reserveIn^2 - reserveIn*reserveOut.
        /// </summary>
        private static decimal ComputeAmmDeltaToPrice(AMMInfo amm, decimal targetQuality)
        {
            var f = AmmInfoExtensions.FeeBpsToDecimal(amm.TradingFee);
            var reserveIn = amm.Amount.ValueAsNumber;
            var reserveOut = amm.Amount2.ValueAsNumber;

            var oneMinusF = 1m - f;
            var a = targetQuality * oneMinusF;
            var b = targetQuality * reserveIn * (2m - f);
            var c = targetQuality * reserveIn * reserveIn - reserveIn * reserveOut;

            var discriminant = b * b - 4m * a * c;
            if (discriminant < 0)
                return 0;

            var sqrtDisc = DecimalMath.Sqrt(discriminant);
            var deltaIn = (-b + sqrtDisc) / (2m * a);
            return deltaIn;
        }
    }
}
