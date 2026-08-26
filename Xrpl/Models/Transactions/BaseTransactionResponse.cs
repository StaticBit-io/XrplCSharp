using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Transactions
{

    /// <summary>
    /// This information is added to Transactions in request responses, but is not part  of the canonical Transaction information on ledger.<br/>
    /// These fields are denoted with  lowercase letters to indicate this in the rippled responses.
    /// </summary>
    public interface IBaseTransactionResponse
    {
        /// <summary>
        /// The ledger close time represented in ISO 8601 time format.
        /// </summary>
        /// <remarks>
        /// rippled attaches this directly to the transaction object on <c>tx</c>/<c>account_tx</c>
        /// responses regardless of API version — API v1 flattens the whole response onto the
        /// transaction, and API v2's <c>account_tx</c> nests it inside <c>tx_json</c> alongside
        /// <c>ctid</c> (unlike the singular <c>tx</c> method, where it sits beside <c>tx_json</c>
        /// instead — see <see cref="Xrpl.Models.Methods.TransactionSummary.CloseTimeIso"/>).
        /// </remarks>
        [JsonConverter(typeof(FromStringDateTimeConverter))]
        [JsonPropertyName("close_time_iso")]
        DateTime? CloseTimeIso { get; set; }

        /// <summary>
        /// The compact transaction identifier of this transaction, when rippled reports one.
        /// </summary>
        /// <remarks>
        /// New in rippled 1.12.0. rippled nests this inside the transaction object itself on
        /// <c>account_tx</c> — see the remark on <see cref="CloseTimeIso"/> for why that placement
        /// differs from the singular <c>tx</c> method's <see cref="Xrpl.Models.Methods.TransactionSummary.Ctid"/>.
        /// </remarks>
        [JsonPropertyName("ctid")]
        string? Ctid { get; set; }

        /// <summary>
        /// The date/time when this transaction was included in a validated ledger.
        /// </summary>
        [JsonConverter(typeof(RippleDateTimeConverter))]
        [JsonPropertyName("date")]
        DateTime? Date { get; set; }

        /// <summary>
        /// An identifying hash value unique to this transaction, as a hex string.
        /// </summary>
        [JsonPropertyName("hash")]
        string Hash { get; set; }

        [JsonPropertyName("inLedger")]
        uint? InLedger { get; set; }

        /// <summary>
        /// The sequence number of the ledger that included this transaction.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        uint? LedgerIndex { get; set; }

        [JsonPropertyName("validated")]
        bool? Validated { get; set; }
    }

    /// <inheritdoc />
    public class BaseTransactionResponse : IBaseTransactionResponse
    {
        /// <inheritdoc />
        [JsonConverter(typeof(FromStringDateTimeConverter))]
        [JsonPropertyName("close_time_iso")]
        public DateTime? CloseTimeIso { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ctid")]
        public string? Ctid { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        [JsonPropertyName("date")]
        public DateTime? Date { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("inLedger")]
        public uint? InLedger { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ledger_index")]
        public uint? LedgerIndex { get; set; }

        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }

        /// <summary>
        /// Members of this transaction response that no declared property claims — for example
        /// a field a new amendment adds before this SDK models it. Populated on
        /// every derived *Response type, and reached whether the response is deserialized directly
        /// or through <see cref="Client.Json.Converters.TransactionResponseConverter"/>: that
        /// converter only picks the concrete .NET type from <c>TransactionType</c>; the field-level
        /// read is the ordinary reflection-based deserializer, which is what honors this attribute.
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
