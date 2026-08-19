using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/ledgerEntry.ts
namespace Xrpl.Models.Ledger
{
    public class LedgerEntryResponse //todo rename LedgerEntryResponse: BaseResponse
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

        [JsonPropertyName("index")]
        public string Index { get; set; }

        [JsonPropertyName("node")]
        [JsonConverter(typeof(LOConverter))]
        public BaseLedgerEntry Node { get; set; }

        //public BaseLedgerEntry LedgerEntry => Node.TryGetValue("LedgerEntryType", out LedgerEntryType type) ? LOConverter.GetBaseRippleLO(type, Node) : null;

        //todo not found fields  - ledger_current_index: number, node?: LedgerEntry,  node_binary?: string,  validated?: boolean
    }
}