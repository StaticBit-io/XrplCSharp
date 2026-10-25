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
                fee = Math.Min(fee, MaxFeeDrops(client.maxFeeXRP) / DROPS_PER_XRP);
            }

            return fee.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads <c>maxFeeXRP</c> as a whole number of drops, floored so a fee rounded up to whole
        /// drops can never exceed the cap the caller set. Shared by <see cref="GetFeeXrp"/> and
        /// Autofill so both treat the same cap the same way.
        /// </summary>
        /// <exception cref="ValidationException">The value is not a number, or is below one drop.</exception>
        internal static decimal MaxFeeDrops(string maxFeeXRP)
        {
            if (!decimal.TryParse(maxFeeXRP, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal maxFee))
            {
                throw new ValidationException($"maxFeeXRP '{maxFeeXRP}' is not a number.");
            }

            decimal drops = Math.Floor(maxFee * DROPS_PER_XRP);
            // A cap below one drop would floor to a zero fee: the transaction could never be
            // applied, and a signed zero-fee blob reports the misconfiguration nowhere.
            if (drops < 1)
            {
                throw new ValidationException($"maxFeeXRP '{maxFeeXRP}' is below one drop (0.000001 XRP).");
            }
            return drops;
        }

        private static decimal CeilingToDrops(decimal xrp) => Math.Ceiling(xrp * DROPS_PER_XRP) / DROPS_PER_XRP;
    }
}

