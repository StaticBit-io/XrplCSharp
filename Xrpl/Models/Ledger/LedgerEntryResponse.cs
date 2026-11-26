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

        /// <summary>
        /// The entry in the binary format, as hexadecimal, when the request set <c>binary</c>.
        /// <see cref="Node"/> is absent then.
        /// </summary>
        [JsonPropertyName("node_binary")]
        public string NodeBinary { get; set; }

        /// <summary>
        /// The ledger index of the ledger version the data was read from. Absent when it was read from
        /// the open ledger; see <see cref="LedgerCurrentIndex"/>.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public uint? LedgerIndex { get; set; }

        /// <summary>
        /// The identifying hash of the ledger version the data was read from. Absent for the open ledger.
        /// </summary>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }

        /// <summary>
        /// The ledger index of the open ledger the data was read from. Present instead of
        /// <c>ledger_index</c> and <c>ledger_hash</c> when the request did not name a closed ledger.
        /// </summary>
        [JsonPropertyName("ledger_current_index")]
        public uint? LedgerCurrentIndex { get; set; }

        /// <summary>
        /// True if the data comes from a validated ledger version, which cannot change.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }
    }
}