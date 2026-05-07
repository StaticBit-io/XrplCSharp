using System;
using System.Text.Json;
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
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

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
        /// The definition of the transaction in JSON format
        /// </summary>
        //[JsonPropertyName("transaction")]
        [JsonPropertyName("tx_json")]
        public object TransactionJson { get; set; }
        /// <summary>
        /// The definition of the proposed transaction in JSON format<br/>
        /// </summary>
        [JsonPropertyName("transaction")]
        public object Proposed { get; set; }

        [JsonIgnore]
        public TransactionResponse Transaction => JsonSerializer.Deserialize<TransactionResponse>((TransactionJson ?? Proposed).ToString(), XrplJsonOptions.Default);

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