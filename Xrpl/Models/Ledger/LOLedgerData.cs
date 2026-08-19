using System.Text.Json;
using System.Text.Json.Serialization;

using System.Collections.Generic;

using Xrpl.Models.Methods;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/ledgerData.ts
namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// The response expected from a <see cref="LedgerDataRequest"/>.
    /// </summary>
    public class LOLedgerData //todo rename to LedgerDataResponse :BaseResponse
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
        [JsonPropertyName("ledger_index")]
        public uint LedgerIndex { get; set; }
        /// <summary>
        /// Unique identifying hash of this ledger version.
        /// </summary>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }
        /// <summary>
        /// Array of JSON objects containing data from the ledger's state tree,  as defined below.
        /// </summary>
        [JsonPropertyName("state")]
        public List<BaseLedgerEntry> State { get; set; }
        /// <summary>
        /// Server-defined value indicating the response is paginated.<br/>
        /// Pass this to  the next call to resume where this call left off.
        /// </summary>
        [JsonPropertyName("marker")]
        public object Marker { get; set; }

        [JsonPropertyName("ledger")]
        public LedgerEntity Ledger { get; set; }
        /// <summary>
        /// True if this data is from a validated ledger version;<br/>
        /// if omitted or set to false, this data is not final.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }
    }
}
