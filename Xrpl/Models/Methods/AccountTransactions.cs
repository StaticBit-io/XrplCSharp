using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transactions;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/accountTx.ts
//https://xrpl.org/account_tx.html
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Expected response from an  <see cref="AccountTransactionsRequest"/>.
    /// </summary>
    public class AccountTransactions  //todo rename to response
    {
        /// <summary>
        /// Unique Address identifying the related account.
        /// </summary>
        [JsonPropertyName("account")]
        public string Account { get; set; }
        /// <summary>
        /// The ledger index of the earliest ledger actually searched for  transactions.
        /// </summary>
        [JsonPropertyName("ledger_index_min")]
        public uint LedgerIndexMin { get; set; }
        /// <summary>
        /// The ledger index of the most recent ledger actually searched for  transactions.
        /// </summary>
        [JsonPropertyName("ledger_index_max")]
        public uint LedgerIndexMax { get; set; }
        /// <summary>
        /// The limit value used in the request.
        /// </summary>
        [JsonPropertyName("limit")]
        public int Limit { get; set; }
        /// <summary>
        /// Server-defined value indicating the response is paginated.<br/>
        /// Pass this  to the next call to resume where this call left off.
        /// </summary>
        [JsonPropertyName("marker")]
        public object Marker { get; set; }
        /// <summary>
        /// Array of transactions matching the request's criteria, as explained  below.
        /// </summary>
        [JsonPropertyName("transactions")]
        public List<TransactionSummary> Transactions { get; set; }
        /// <summary>
        /// If included and set to true, the information in this response comes from  a validated ledger version.<br/>
        /// Otherwise, the information is subject to  change.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool Validated { get; set; }
    }

    public interface IAccountTransaction
    {
        /// <summary>
        /// The ledger close time represented in ISO 8601 time format.
        /// </summary>
        public DateTime? CloseTimeIso { get; set; }

        /// <summary>
        /// The unique hash identifier of the transaction.
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// (Validated transactions only) The identifying hash of the ledger version that includes this transaction
        /// </summary>
        public string LedgerHash { get; set; }

        /// <summary>
        /// (Validated transactions only) The ledger index of the ledger version that includes this transaction.
        /// </summary>
        public ulong? LedgerIndex { get; set; }

        /// <summary>
        /// (Validated transactions only) The transaction metadata, which shows the exact outcome of the transaction in detail.
        /// </summary>
        public Meta Meta { get; set; }
        /// <summary>
        /// (JSON mode) JSON object defining the transaction.
        /// </summary>
        public TransactionResponse Transaction { get; }

        /// <summary>
        /// If true, this transaction is included in a validated ledger and its outcome is final.<br/>
        /// Responses from the transaction stream should always be validated.
        /// </summary>
        public bool Validated { get; set; }
    }

    public class TransactionSummary : IAccountTransaction //todo rename to AccountTransaction
    {
        private TransactionResponse _transaction;
        private string _hash;
        private ulong? _ledgerIndex;

        /// <summary>
        /// The ledger close time represented in ISO 8601 time format.
        /// </summary>
        /// <remarks>API v2 only — API v1 does not report the close time on account_tx entries.</remarks>
        [JsonPropertyName("close_time_iso")]
        [JsonConverter(typeof(FromStringDateTimeConverter))]
        public DateTime? CloseTimeIso { get; set; }

        /// <summary>
        /// The compact transaction identifier, when rippled reports one.
        /// </summary>
        /// <remarks>
        /// Covers the singular <c>tx</c> method, where <c>ctid</c> sits beside <c>tx_json</c> —
        /// this property reads it from there. <c>account_tx</c> instead nests <c>ctid</c> inside
        /// <c>tx_json</c> itself, which lands on
        /// <see cref="Xrpl.Models.Transactions.IBaseTransactionResponse.Ctid"/> on the deserialized
        /// transaction, not here.
        /// </remarks>
        [JsonPropertyName("ctid")]
        public string? Ctid { get; set; }

        /// <summary>
        /// If binary is True, then this is a hex string of the transaction metadata.
        /// </summary>
        /// <remarks>
        /// API v2 with <c>binary: true</c>. rippled sends this as a top-level sibling of
        /// <c>tx_blob</c> instead of the usual <c>meta</c> field, and the <c>meta</c> field is
        /// absent entirely — so <see cref="MetaBinaryConverter"/>'s string branch, which handles
        /// API v1's <c>"meta": "&lt;hex&gt;"</c>, never runs for this shape. API v1 binary mode
        /// puts the same hex string in <see cref="Meta.MetaBlob"/> instead, reached through
        /// <see cref="Meta"/> below.
        /// </remarks>
        [JsonPropertyName("meta_blob")]
        public string? MetaBlob { get; set; }

        /// <summary>
        /// If binary is True, then this is a hex string of the transaction itself.
        /// </summary>
        /// <remarks>
        /// API v2 with <c>binary: true</c>. rippled sends this as a top-level sibling of
        /// <c>meta_blob</c> instead of the usual <c>tx_json</c> field, and <c>tx_json</c> is
        /// absent entirely — so <see cref="Transaction"/> below is null for this shape.
        /// </remarks>
        [JsonPropertyName("tx_blob")]
        public string? TxBlob { get; set; }

        /// <summary>
        /// A hex string of the ledger version that included this transaction.
        /// </summary>
        /// <remarks>API v2 only — API v1 does not report the ledger hash on account_tx entries.</remarks>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }
        /// <summary>
        /// The ledger index of the ledger version that included this transaction.
        /// </summary>
        /// <remarks>
        /// API v1 reports this inside the transaction envelope instead of at the top level,
        /// so it falls back to the deserialized transaction.
        /// </remarks>
        [JsonPropertyName("ledger_index")]
        public ulong? LedgerIndex
        {
            get => _ledgerIndex ?? _transaction?.LedgerIndex;
            set => _ledgerIndex = value;
        }
        /// <summary>
        /// If binary is True, then this is a hex string of the transaction metadata.<br/>
        /// Otherwise, the transaction metadata is included in JSON format.
        /// </summary>
        [JsonPropertyName("meta")]
        [JsonConverter(typeof(MetaBinaryConverter))]
        public Meta Meta { get; set; }
        /// <summary>
        /// JSON object defining the transaction.
        /// </summary>
        /// <remarks>
        /// rippled wraps the transaction in <c>tx_json</c> under API v2 and in <c>tx</c> under API v1;
        /// both envelopes populate this property.
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
        [JsonPropertyName("tx")]
        private TransactionResponse TransactionV1
        {
            set => _transaction = value ?? _transaction;
        }

        /// <summary>
        /// Unique hashed String representing the transaction.
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
        /// Whether or not the transaction is included in a validated ledger.<br/>
        /// Any transaction not yet in a validated ledger is subject to change.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool Validated { get; set; }
    }
    /// <summary>
    /// The account_tx method retrieves a list of transactions that involved the  specified account.<br/>
    /// Expects a response in the form of a  <see cref="AccountTransactions"/>.
    /// </summary>
    /// <code>
    /// {
    /// 	"id": 2,
    /// 	"command": "account_tx",
    /// 	"account": "rLNaPoKeeBjZe2qs6x52yVPZpZ8td4dc6w",
    /// 	"ledger_index_min": -1,
    /// 	"ledger_index_max": -1,
    /// 	"binary": false,
    /// 	"limit": 2,
    /// 	"forward": false
    /// }
    /// </code>
    public class AccountTransactionsRequest : BaseLedgerRequest
    {
        public AccountTransactionsRequest(string account)
        {
            Account = account;
            Command = "account_tx";
            LedgerIndexMin = -1;
            LedgerIndexMax = -1;
        }
        /// <summary>
        /// A unique identifier for the account, most commonly the account's address.
        /// </summary>
        [JsonPropertyName("account")]
        public string Account { get; set; }
        /// <summary>
        /// Use to specify the earliest ledger to include transactions from.<br/>
        /// A value of -1 instructs the server to use the earliest validated ledger version available.
        /// </summary>
        [JsonPropertyName("ledger_index_min")]
        public int? LedgerIndexMin { get; set; }
        /// <summary>
        /// Use to specify the most recent ledger to include transactions from.<br/>
        /// A value of -1 instructs the server to use the most recent validated ledger version available.
        /// </summary>
        [JsonPropertyName("ledger_index_max")]
        public int? LedgerIndexMax { get; set; }
        /// <summary>
        /// If true, return transactions as hex strings instead of JSON.<br/>
        /// The default is false.
        /// </summary>
        [JsonPropertyName("binary")]
        public bool? Binary { get; set; }
        /// <summary>
        /// If true, returns values indexed with the oldest ledger first.<br/>
        /// Otherwise, the results are indexed with the newest ledger first.
        /// </summary>
        [JsonPropertyName("forward")]
        public bool? Forward { get; set; }
        /// <summary>
        /// Default varies.<br/>
        /// Limit the number of transactions to retrieve.<br/>
        /// The server is not required to honor this value.
        /// </summary>
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }
        /// <summary>
        /// Value from a previous paginated response.<br/>
        /// Resume retrieving data where that response left off.<br/>
        /// This value is stable even if there is a change in the server's range of available ledgers.
        /// </summary>
        [JsonPropertyName("marker")]
        public object Marker { get; set; }

        /// <summary>
        /// Optional) Clio Only Return only transactions of a specific type,<br/>
        /// such as "Clawback", "AccountSet", "AccountDelete", et al. Case-insensitive.<br/>
        /// Supports any transaction type except AMM* (See Transaction Types https://xrpl.org/transaction-types.html)
        /// </summary>
        [JsonPropertyName("tx_type")]
        public string TxType { get; set; }
    }
}
