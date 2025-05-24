using Newtonsoft.Json;

using Xrpl.Client.Json.Converters;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/ledgerEntry.ts
namespace Xrpl.Models.Ledger
{
    public class LedgerEntryResponse //todo rename LedgerEntryResponse: BaseResponse
    {
        [JsonProperty("index")]
        public string Index { get; set; }

        [JsonProperty("node")]
        [JsonConverter(typeof(LOConverter))]
        public BaseLedgerEntry Node { get; set; }

        //public BaseLedgerEntry LedgerEntry => Node.TryGetValue("LedgerEntryType", out LedgerEntryType type) ? LOConverter.GetBaseRippleLO(type, Node) : null;

        //todo not found fields  - ledger_current_index: number, node?: LedgerEntry,  node_binary?: string,  validated?: boolean
    }
}