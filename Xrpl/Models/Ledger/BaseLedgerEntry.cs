using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

//https://github.com/XRPLF/xrpl.js/blob/76b73e16a97e1a371261b462ee1a24f1c01dbb0c/packages/xrpl/src/models/ledger/BaseLedgerEntry.ts

namespace Xrpl.Models.Ledger
{
    public class BaseLedgerEntry
    {

        [JsonConverter(typeof(StringEnumConverter))]
        public LedgerEntryType LedgerEntryType { get; set; }

        /// <summary>
        /// The unique ID for this ledger entry.<br/>
        /// In JSON, this field is represented with different names depending on the context and API method.<br/>
        /// (Note, even though this is specified as "optional" in the code, every ledger entry should have one unless it's legacy data from very early in the XRP Ledger's history.)
        /// </summary>
        [JsonProperty("index")]
        public string Index { get; set; }
        [JsonProperty("LedgerIndex")]
        public string LedgerIndex { get; set; }
    }
}