using System;
using System.Collections.Generic;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// rippled's <c>RippleCalc::rippleCalculate</c> and <c>flow</c>: a payment's strands built and
    /// run against a view, the result written into it when the flow succeeds.
    /// </summary>
    internal static class RippleCalc
    {
        internal sealed class Output
        {
            internal string Result { get; init; }

            /// <summary>What left the source, in the send-max asset.</summary>
            internal XrplAmount ActualIn { get; init; }

            /// <summary>What reached the destination.</summary>
            internal XrplAmount ActualOut { get; init; }

            internal bool Succeeded => Result == "tesSUCCESS";
        }

        /// <param name="view">The view to run against; a successful flow is applied to it.</param>
        /// <param name="maxAmount">The most to send; a negative amount means no limit.</param>
        /// <param name="deliver">What to deliver.</param>
        /// <param name="destination">The receiving account.</param>
        /// <param name="source">The sending account.</param>
        /// <param name="paths">The paths, besides the default one.</param>
        /// <param name="domainId">The permissioned domain whose books to walk; null for the open ones.</param>
        /// <param name="defaultPaths">Whether the default path is tried too.</param>
        /// <param name="partialPayment">Whether less than <paramref name="deliver"/> is a success.</param>
        /// <param name="limitQuality">Whether no strand may be worse than <paramref name="maxAmount"/> for <paramref name="deliver"/>.</param>
        internal static Output Calculate(
            DexView view,
            XrplAmount maxAmount,
            XrplAmount deliver,
            string destination,
            string source,
            IReadOnlyList<IReadOnlyList<PathElement>> paths,
            string domainId,
            bool defaultPaths,
            bool partialPayment,
            bool limitQuality = false)
        {
            XrplQuality? limit = limitQuality && StepMath.IsPositive(maxAmount)
                ? XrplQuality.FromAmounts(maxAmount, deliver, view.Rules)
                : null;

            // A negative maximum of the delivered currency, issued by the source, is no maximum at all.
            bool sameToken = XrplAmount.KindOf(maxAmount.Asset) == XrplAmount.KindOf(deliver.Asset) &&
                             (XrplAmount.KindOf(maxAmount.Asset) == AmountKind.Xrp ||
                              string.Equals(maxAmount.Asset.Currency, deliver.Asset.Currency, StringComparison.Ordinal));
            XrplAmount? sendMax = !maxAmount.IsNegative || !sameToken ||
                                  !string.Equals(IssuerOf(maxAmount.Asset), source, StringComparison.Ordinal)
                ? maxAmount
                : null;

            IssuedCurrency sourceAsset = sendMax?.Asset ?? (deliver.Kind == AmountKind.Xrp
                ? deliver.Asset
                : new IssuedCurrency { Currency = deliver.Asset.Currency, Issuer = source });

            AmmFlowContext ammContext = new AmmFlowContext(source);
            StrandBuilder.Request request = new StrandBuilder.Request
            {
                Source = source,
                Destination = destination,
                Deliver = deliver.Asset,
                SendMax = sendMax?.Asset,
                LimitQuality = limit,
                OfferCrossing = false,
                AmmContext = ammContext,
                DomainId = domainId,
            };

            try
            {
                (string strandsResult, List<List<FlowStep>> strands) = StrandBuilder.ToStrands(view, request, paths, defaultPaths);
                if (strandsResult != null)
                    return Failed(strandsResult, sourceAsset, deliver.Asset);

                ammContext.MultiPath = strands.Count > 1;
                FlowResult flow = StrandFlow.Run(view, strands, sourceAsset, deliver, partialPayment, CrossingMode.No, limit, sendMax, ammContext);
                if (flow.Succeeded)
                    flow.Sandbox.ApplyTo(view);

                return new Output
                {
                    Result = flow.Result,
                    ActualIn = flow.In.Asset == null ? XrplAmount.Zero(sourceAsset) : flow.In,
                    ActualOut = flow.Out.Asset == null ? XrplAmount.Zero(deliver.Asset) : flow.Out,
                };
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException or DivideByZeroException)
            {
                return Failed("tecINTERNAL", sourceAsset, deliver.Asset);
            }
        }

        private static Output Failed(string result, IssuedCurrency sourceAsset, IssuedCurrency deliverAsset) =>
            new Output { Result = result, ActualIn = XrplAmount.Zero(sourceAsset), ActualOut = XrplAmount.Zero(deliverAsset) };

        private static string IssuerOf(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) == AmountKind.Xrp ? string.Empty : asset.Issuer;
    }
}
