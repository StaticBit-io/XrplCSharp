using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/getFeeXrp.ts

namespace Xrpl.Sugar
{
    public static class GetFeeXrpSugar
    {
        private const decimal DROPS_PER_XRP = 1_000_000m;

        /// <summary>
        /// Calculates the current transaction fee for the ledger.
        /// Note: This is a public API that can be called directly.
        /// </summary>
        /// <param name="client">The Client used to connect to the ledger.</param>
        /// <param name="cushion">The fee cushion to use</param>
        /// <returns>The transaction fee in XRP, rounded up to whole drops and capped at <c>maxFeeXRP</c> (floored to whole drops).</returns>
        public static async Task<string> GetFeeXrp(this IXrplClient client, double? cushion = null, CancellationToken cancellationToken = default)
        {
            double feeCushion = cushion ?? client.feeCushion;
            ServerInfoRequest request = new ServerInfoRequest();
            ServerInfo serverInfo = await client.ServerInfo(request, cancellationToken).Typed();
            decimal? baseFee = serverInfo.Info.ValidatedLedger?.BaseFeeXrp;
            if (baseFee == null)
            {
                throw new XrplException("getFeeXrp: Could not get base_fee_xrp from server_info");
            }

            decimal baseFeeXrp = baseFee.Value;

            if (serverInfo.Info.LoadFactor == null)
            {
                // https://github.com/ripple/rippled/issues/3812#issuecomment-816871100
                serverInfo.Info.LoadFactor = 1;
            }

            decimal fee = baseFeeXrp * (decimal)serverInfo.Info.LoadFactor * (decimal)feeCushion;

            // A fractional load_factor (1.03515625 is a common one) leaves digits below one drop,
            // which XrpToDrops rejects. xrpl.js rounds half-up to 6 places; rounding up instead
            // keeps the fee from landing below base_fee * load_factor when the cushion is 1.
            fee = CeilingToDrops(fee);

            // Cap fee to `client.maxFeeXRP`
            if (!string.IsNullOrWhiteSpace(client.maxFeeXRP))
            {
                // Floored so the rounded-up fee can never exceed the cap the caller set.
                decimal maxFee = decimal.Parse(client.maxFeeXRP, NumberStyles.Float, CultureInfo.InvariantCulture);
                fee = Math.Min(fee, FloorToDrops(maxFee));
            }

            return fee.ToString(CultureInfo.InvariantCulture);
        }

        private static decimal CeilingToDrops(decimal xrp) => Math.Ceiling(xrp * DROPS_PER_XRP) / DROPS_PER_XRP;

        private static decimal FloorToDrops(decimal xrp) => Math.Floor(xrp * DROPS_PER_XRP) / DROPS_PER_XRP;
    }
}

