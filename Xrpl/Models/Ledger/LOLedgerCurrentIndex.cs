
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/ledgerCurrent.ts
namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// Response expected from a {@link LedgerCurrentRequest}.
    /// </summary>
    public class LOLedgerCurrentIndex //todo rename to LedgerCurrentResponse
    {
        /// <summary>
        /// Members the node sent that no declared property here claims. Mirrors
        /// <see cref="BaseLedgerEntry.UnknownFields"/> for ledger entries and
        /// <see cref="Xrpl.Models.Methods.BaseMethodResult.UnknownFields"/> for command results:
        /// without it, anything this model does not yet know about is dropped between the node and
        /// the caller instead of surviving the round trip.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> UnknownFields { get; set; }

        /// <summary>
        /// The ledger index of this ledger version.
        /// </summary>
        [JsonPropertyName("ledger_current_index")]
        public uint CurrentIndex { get; set; }
    }
}
