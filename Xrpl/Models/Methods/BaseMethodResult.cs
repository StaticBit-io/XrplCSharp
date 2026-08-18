using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Shared base for rippled command result models that have no ledger-entry or transaction
    /// envelope base of their own to carry unknown fields (compare
    /// <see cref="Xrpl.Models.Ledger.BaseLedgerEntry.UnknownFields"/> and
    /// <see cref="Xrpl.Models.Transactions.BaseTransactionResponse.UnknownFields"/>, which cover
    /// those two families). Every <c>result</c> shape below this class is deserialized directly by
    /// the ordinary reflection-based deserializer - none of it goes through a type-dispatching
    /// converter - so this attribute alone is enough to stop members the model does not declare a
    /// property for from silently vanishing.
    /// </summary>
    /// <remarks>
    /// Not a substitute for <c>XrplResponse&lt;T&gt;.Raw</c>: values here have already gone
    /// through JSON parsing (numbers, strings, nested objects as <see cref="JsonElement"/>), while
    /// <c>Raw</c> is the exact bytes the node sent. Use <c>Raw</c> when byte-for-byte fidelity
    /// matters; use this when a caller just needs to read a field the model does not yet declare.
    /// </remarks>
    public class BaseMethodResult
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> UnknownFields { get; set; }
    }
}
