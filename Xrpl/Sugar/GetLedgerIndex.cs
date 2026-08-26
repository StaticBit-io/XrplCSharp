using System;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

using Xrpl.Client.Exceptions;
// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/sugar/getLedgerIndex.ts

namespace Xrpl.Sugar
{
    public static class GetLedgerSugar
    {
        /// <summary>
        /// Returns the index of the most recently validated ledger.
        /// </summary>
        /// <param name="client">The Client used to connect to the ledger.</param>
        // <returns>The most recently validated ledger index.</returns>
        public static async Task<uint> GetLedgerIndex(this IXrplClient client, CancellationToken cancellationToken = default)
        {
            LedgerIndex index = new LedgerIndex(LedgerIndexType.Current);
            LedgerRequest request = new LedgerRequest() { LedgerIndex = index };
            LOLedger ledgerResponse = await client.Ledger(request, cancellationToken).Typed();
            // See DomainAccess for why this is checked rather than cast: a missing "ledger" member
            // casts to null and faults later, a binary response is a different concrete type.
            if (ledgerResponse.LedgerEntity is not LedgerEntity ledger)
            {
                throw new ValidationException(
                    "Ledger response did not include a JSON ledger object"
                    + (ledgerResponse.LedgerEntity is null ? "." : " - got " + ledgerResponse.LedgerEntity.GetType().Name + ", which a binary request produces."));
            }

            return Convert.ToUInt32(ledger.LedgerIndex);
        }
    }
}

