using System;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

//https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/methods/subscribe.ts#L253
namespace Xrpl.Models.Subscriptions
{
    /// <summary>
    /// Many subscriptions result in messages about transactions, including the following:
    /// The transactions stream <br/>
    /// The transactions_proposed stream<br/>
    /// accounts subscriptions<br/>
    /// accounts_proposed subscriptions<br/>
    /// book (Order Book) subscriptions
    /// <see href="https://xrpl.org/subscribe.html#transaction-streams"/>
    /// </summary>
    public class TransactionStream : BaseStream, IAccountTransaction
    {
        private TransactionResponse _transaction;
        private string _hash;

        /// <summary>
        /// The ledger close time represented in ISO 8601 time format.
        /// </summary>
        [JsonPropertyName("close_time_iso")]
        public DateTime? CloseTimeIso { get; set; }

        /// <summary>
        /// String Transaction result code
        /// </summary>
        [JsonPropertyName("engine_result")]
        public string EngineResult { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// The compact transaction identifier of this transaction, when rippled reports one.
        /// </summary>
        /// <remarks>
        /// rippled's <c>NetworkOpsImp::transJson</c> writes this at the top level of the stream
        /// event (<c>jvObj[jss::ctid]</c> in NetworkOPs.cpp), not inside <c>tx_json</c>/<c>transaction</c>
        /// - unlike <c>account_tx</c>, which nests it inside the transaction envelope and lands on
        /// <see cref="Methods.TransactionSummary.Ctid"/> instead. A dedicated property rather than
        /// leaving it to <see cref="UnknownFields"/>: a wallet identifying "which transaction is
        /// this" needs it typed, the same way <see cref="Hash"/> is, not fished out of a dictionary.
        /// </remarks>
        [JsonPropertyName("ctid")]
        public string Ctid { get; set; }

        /// <summary>
        /// Numeric transaction response code, if applicable.
        /// </summary>
        [JsonPropertyName("engine_result_code")]
        public int EngineResultCode { get; set; }
        /// <summary>
        /// Human-readable explanation for the transaction response
        /// </summary>
        [JsonPropertyName("engine_result_message")]
        public string EngineResultMessage { get; set; }

        /// <summary>
        /// The unique hash identifier of the transaction.
        /// </summary>
        /// <remarks>
        /// API v1 reports the hash inside the transaction envelope instead of at the top level,
        /// so it falls back to the deserialized transaction.
        /// </remarks>
        [JsonPropertyName("hash")]
        public string Hash
        {
            get => _hash ?? _transaction?.Hash;
            set => _hash = value;
        }

        /// <summary>
        /// (Validated transactions only) The identifying hash of the ledger version that includes this transaction
        /// </summary>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }
        /// <summary>
        /// (Validated transactions only) The ledger index of the ledger version that includes this transaction.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public ulong? LedgerIndex { get; set; }
        /// <summary>
        /// (Unvalidated transactions only) The ledger index of the current in-progress ledger version for which this transaction is currently proposed.
        /// </summary>
        [JsonPropertyName("ledger_current_index")]
        public uint? LedgerCurrentIndex { get; set; }
        /// <summary>
        /// (Validated transactions only) The transaction metadata, which shows the exact outcome of the transaction in detail.
        /// </summary>
        [JsonPropertyName("meta")]
        public Meta Meta { get; set; }
        /// <summary>
        /// The definition of the transaction in JSON format.
        /// </summary>
        /// <remarks>
        /// rippled wraps the transaction in <c>tx_json</c> under API v2 and in <c>transaction</c>
        /// under API v1; both envelopes populate this property. It is deserialized once, with the
        /// message that carries it - reading it back costs nothing.
        /// </remarks>
        [JsonPropertyName("tx_json")]
        public TransactionResponse Transaction
        {
            get => _transaction;
            set => _transaction = value ?? _transaction;
        }

        /// <summary>
        /// API v1 envelope for <see cref="Transaction"/>.
        /// </summary>
        /// <remarks>
        /// Set-only alias: it never appears in serialized output. [JsonInclude] is required because
        /// System.Text.Json ignores non-public members without it.
        /// </remarks>
        [JsonInclude]
        [JsonPropertyName("transaction")]
        private TransactionResponse TransactionV1
        {
            set => _transaction = value ?? _transaction;
        }

        private JsonSlice _transactionSlice;

        /// <summary>
        /// The transaction exactly as the node sent it — <c>tx_json</c> under API v2,
        /// <c>transaction</c> under API v1.
        /// </summary>
        /// <remarks>
        /// Empty when this event was never paired with a frame, or the message carried neither
        /// envelope (a stream message reporting neither <c>tx_json</c> nor <c>transaction</c>).
        /// </remarks>
        [JsonIgnore]
        public RawJson RawTransaction =>
            _frame is null || _transactionSlice.IsEmpty
                ? default
                : new RawJson(_frame, _transactionSlice.Offset, _transactionSlice.Length);

        /// <inheritdoc/>
        /// <remarks>
        /// <see cref="Transaction"/> and its API v1 alias already claim <c>tx_json</c> and
        /// <c>transaction</c> - System.Text.Json rejects a second member bound to a name another
        /// member already owns, so <see cref="_transactionSlice"/> cannot be filled the way
        /// <see cref="BaseResponse.ResultSlice"/> is, through a converter-backed property. Instead
        /// this scans the frame directly with <see cref="JsonSlice.FindTopLevelMember"/>, after the
        /// object graph (<see cref="Transaction"/> included) has already been built from it -
        /// finding no more than what deserialization itself just read.
        /// </remarks>
        internal override void AttachFrame(byte[] frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            JsonSlice slice = JsonSlice.FindTopLevelMember(frame, "tx_json"u8);
            _transactionSlice = slice.IsEmpty ? JsonSlice.FindTopLevelMember(frame, "transaction"u8) : slice;
            base.AttachFrame(frame);
        }

        /// <summary>
        /// If true, this transaction is included in a validated ledger and its outcome is final.<br/>
        /// Responses from the transaction stream should always be validated.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool Validated { get; set; }

        /// <summary>
        /// May be omitted) If this field is provided, it contains one or more Warnings Objects with important warnings.<br/>
        /// For details, see API Warnings (https://xrpl.org/response-formatting.html#api-warnings)
        /// </summary>
        [JsonPropertyName("warnings")]
        public object Warnings { get; set; }
    }
}