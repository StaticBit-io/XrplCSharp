using System.Collections.Generic;
using System.Text.Json.Serialization;

// https://github.com/XRPLF/clio/blob/develop/src/rpc/handlers/NFTHistory.cpp
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Response expected from an <see cref="NFTHistoryRequest"/>.
    /// </summary>
    public class NFTHistory : BaseMethodResult
    {
        /// <summary>
        /// The token whose history this is.
        /// </summary>
        [JsonPropertyName("nft_id")]
        public string NFTokenID { get; set; }

        /// <summary>
        /// The earliest ledger actually searched.
        /// </summary>
        [JsonPropertyName("ledger_index_min")]
        public uint? LedgerIndexMin { get; set; }

        /// <summary>
        /// The most recent ledger actually searched.
        /// </summary>
        [JsonPropertyName("ledger_index_max")]
        public uint? LedgerIndexMax { get; set; }

        /// <summary>
        /// The limit that was applied.
        /// </summary>
        [JsonPropertyName("limit")]
        public uint? Limit { get; set; }

        /// <summary>
        /// Present when there is more to read; pass it back to continue where this left off.
        /// </summary>
        /// <remarks>
        /// Clio sends an object of <c>ledger</c> and <c>seq</c> here, the same marker
        /// <c>account_tx</c> uses. Typed as <c>object</c> for the same reason it is there: the
        /// server defines its shape, and a caller's business with it is to hand it back unread.
        /// </remarks>
        [JsonPropertyName("marker")]
        public object Marker { get; set; }

        /// <summary>
        /// The transactions that touched this token, newest first unless <c>forward</c> was asked for.
        /// </summary>
        /// <remarks>
        /// The same entries <c>account_tx</c> returns, so the same type reads them - including the
        /// <c>tx</c> versus <c>tx_json</c> envelopes of API v1 and v2, which
        /// <see cref="TransactionSummary"/> already handles. Match them on the <c>I</c>-interfaces,
        /// as with any transaction read back from a ledger.
        /// </remarks>
        [JsonPropertyName("transactions")]
        public List<TransactionSummary> Transactions { get; set; }

        /// <summary>
        /// Whether the answer comes from validated ledgers.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }
    }

    /// <summary>
    /// The <c>nft_history</c> method asks what has happened to a token.
    /// </summary>
    /// <remarks>
    /// A Clio method, like <see cref="NFTInfoRequest"/>: a plain rippled node answers
    /// <c>unknownCmd</c>. Paginated the way <c>account_tx</c> is - keep passing
    /// <see cref="NFTHistory.Marker"/> back until the answer comes without one.
    /// </remarks>
    public class NFTHistoryRequest : BaseLedgerRequest
    {
        public NFTHistoryRequest(string nft_id)
        {
            NFTokenID = nft_id;
            Command = "nft_history";
        }

        /// <summary>
        /// The unique identifier of the NFToken whose history to read.
        /// </summary>
        [JsonPropertyName("nft_id")]
        public string NFTokenID { get; set; }

        /// <summary>
        /// The earliest ledger to search. <c>-1</c> asks for the earliest available.
        /// </summary>
        [JsonPropertyName("ledger_index_min")]
        public int? LedgerIndexMin { get; set; }

        /// <summary>
        /// The most recent ledger to search. <c>-1</c> asks for the most recent available.
        /// </summary>
        [JsonPropertyName("ledger_index_max")]
        public int? LedgerIndexMax { get; set; }

        /// <summary>
        /// Return transactions as hex strings instead of JSON.
        /// </summary>
        [JsonPropertyName("binary")]
        public bool? Binary { get; set; }

        /// <summary>
        /// Read oldest first instead of newest first.
        /// </summary>
        [JsonPropertyName("forward")]
        public bool? Forward { get; set; }

        /// <summary>
        /// How many transactions to return at most.
        /// </summary>
        [JsonPropertyName("limit")]
        public uint? Limit { get; set; }

        /// <summary>
        /// The marker from a previous answer, to continue from where it stopped.
        /// </summary>
        [JsonPropertyName("marker")]
        public object Marker { get; set; }
    }
}
