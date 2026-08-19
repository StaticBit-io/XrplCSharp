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
        /// <summary>
        /// Members of the result that no declared property on the concrete response type claims -
        /// a field an amendment adds before this SDK models it, or anything else unrecognized.
        /// Declared here rather than repeated on every subclass, mirroring
        /// <see cref="Xrpl.Models.Ledger.BaseLedgerEntry.UnknownFields"/> and
        /// <see cref="Xrpl.Models.Transactions.BaseTransactionResponse.UnknownFields"/> for their
        /// own families.
        /// </summary>
        /// <remarks>
        /// Values here have already gone through JSON parsing - see the class remarks above for
        /// how that differs from <c>XrplResponse&lt;T&gt;.Raw</c>. That parsing has a real
        /// retention cost, out of proportion to the unknown member's own size: a single large
        /// unrecognized value held here alone raised one captured response's retained size from
        /// roughly 36 700 B to 65 704 B - about 1.79x, not merely the member's bytes added on top -
        /// because a <see cref="JsonElement"/> keeps a reference into the pooled buffer backing the
        /// <see cref="JsonDocument"/> it was parsed from rather than owning a right-sized copy.
        /// Accepted anyway: the alternative is losing the field outright, which is worse for a
        /// caller relying on this to read a member the model does not yet declare.
        ///
        /// The figure above is for one large member; the cost that actually bites is per-member
        /// and multiplies by nesting. Each captured member costs about 464 B regardless of how
        /// small its JSON is, so a page of 1 000 nested objects each carrying one unmodelled field
        /// retains 792 KB against 320 KB when the field was dropped - 4.33x the JSON's own size
        /// rather than 1.75x. Where a field is known to arrive on every message of a busy stream,
        /// declare a property for it instead of relying on this - see
        /// <see cref="Xrpl.Models.Subscriptions.TransactionStream.AccountHistoryTxIndex"/>.
        /// </remarks>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> UnknownFields { get; set; }
    }
}
