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
        /// leaving it to <see cref="BaseStream.UnknownFields"/>: a wallet identifying "which transaction is
        /// this" needs it typed, the same way <see cref="Hash"/> is, not fished out of a dictionary.
        /// </remarks>
        [JsonPropertyName("ctid")]
        public string Ctid { get; set; }

        /// <summary>
        /// Position of this transaction within an <c>account_history</c> subscription, when the
        /// event comes from one.
        /// </summary>
        /// <remarks>
        /// Signed on purpose. rippled counts forward from zero while streaming new transactions
        /// (<c>forwardTxIndex++</c>, a <c>uint32</c>) and counts *down* through the same zero while
        /// backfilling history (<c>txHistoryIndex--</c>, NetworkOPs.cpp), so a backfilled event
        /// carries a negative index.
        ///
        /// Declared rather than left to <see cref="BaseStream.UnknownFields"/> along with the two
        /// flags below: rippled sends all three on every event of such a subscription, and capture
        /// costs about 464 B per member because each unknown value is parsed into its own
        /// <see cref="System.Text.Json.JsonDocument"/>. Measured at ~796 B per event for the three
        /// together - paid on every transaction a wallet receives, which is the one path where
        /// that is least affordable.
        /// </remarks>
        [JsonPropertyName("account_history_tx_index")]
        public long? AccountHistoryTxIndex { get; set; }

        /// <summary>
        /// Present and <c>true</c> when this transaction is the last one of its ledger within an
        /// <c>account_history</c> stream - the marker a consumer batches on.
        /// </summary>
        [JsonPropertyName("account_history_boundary")]
        public bool? AccountHistoryBoundary { get; set; }

        /// <summary>
        /// Present and <c>true</c> on the earliest transaction that ever touched the subscribed
        /// account, which is how a consumer knows the backfill has reached the end.
        /// </summary>
        [JsonPropertyName("account_history_tx_first")]
        public bool? AccountHistoryTxFirst { get; set; }

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
                : RawJson.Trusted(_frame, _transactionSlice.Offset, _transactionSlice.Length);

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

            JsonSlice txJson = JsonSlice.FindTopLevelMember(frame, "tx_json"u8, ignoringJsonNull: true);
            JsonSlice legacy = JsonSlice.FindTopLevelMember(frame, "transaction"u8, ignoringJsonNull: true);

            // Whichever envelope sits later in the frame wins, because that is what the typed
            // Transaction ends up holding: its two setters both do `value ?? _transaction` and run
            // in document order, so the last non-null one assigned is the one that survives.
            // Preferring tx_json unconditionally would let RawTransaction show the caller one
            // transaction while Transaction carried another - the same show-one/sign-another split
            // that duplicate keys were already fixed for in JsonSlice.FindTopLevelMember. rippled
            // never sends both (NetworkOPs.cpp transJson moves transaction to tx_json under API v2
            // rather than adding it), but the frame arrives over the network through arbitrary
            // infrastructure, so the two views must not be able to disagree.
            _transactionSlice =
                txJson.IsEmpty ? legacy
                : legacy.IsEmpty ? txJson
                : legacy.Offset > txJson.Offset ? legacy : txJson;

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