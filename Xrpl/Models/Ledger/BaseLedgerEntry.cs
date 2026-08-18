using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;

//https://github.com/XRPLF/xrpl.js/blob/76b73e16a97e1a371261b462ee1a24f1c01dbb0c/packages/xrpl/src/models/ledger/BaseLedgerEntry.ts

namespace Xrpl.Models.Ledger
{
    public class BaseLedgerEntry
    {

        // Nullable: this model also represents PreviousFields/FinalFields content, where the node omits LedgerEntryType.
        [JsonConverter(typeof(LedgerEntryTypeConverter))]
        public LedgerEntryType? LedgerEntryType { get; set; }

        /// <summary>
        /// The unique ID for this ledger entry.<br/>
        /// In JSON, this field is represented with different names depending on the context and API method.<br/>
        /// (Note, even though this is specified as "optional" in the code, every ledger entry should have one unless it's legacy data from very early in the XRP Ledger's history.)
        /// </summary>
        [JsonPropertyName("index")]
        public string Index { get; set; }
        [JsonPropertyName("LedgerIndex")]
        public string LedgerIndex { get; set; }

        /// <summary>
        /// Members of this ledger entry that no declared property claims — new fields an amendment
        /// added to the wire format before this SDK modeled them, or anything else unrecognized.
        /// Populated on every derived LO* type, and on FinalFields/PreviousFields/NewFields reached
        /// through <see cref="Client.Json.Converters.LOConverter"/> and the node converters, because
        /// those converters only pick the concrete .NET type; the field-level read is the ordinary
        /// reflection-based deserializer, which is what honors this attribute.
        /// </summary>
        /// <remarks>
        /// This is not a substitute for <c>XrplResponse&lt;T&gt;.Raw</c>: values here have already
        /// gone through JSON parsing (numbers, strings, nested objects as <see cref="JsonElement"/>),
        /// while <c>Raw</c> is the exact bytes the node sent. Use <c>Raw</c> when byte-for-byte
        /// fidelity matters (verifying what a node actually said); use this when a caller just needs
        /// to read a field the model does not yet declare.
        /// </remarks>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> UnknownFields { get; set; }
    }
}