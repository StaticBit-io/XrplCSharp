using System.Text.Json;
using System.Text.Json.Serialization;

// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/transaction-methods/transaction_entry

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// The <c>transaction_entry</c> method retrieves information on a single transaction
    /// from a specific ledger version. It differs from the <c>tx</c> method in that it
    /// requires you to specify the ledger version to search.
    /// </summary>
    public class TransactionEntryRequest : BaseLedgerRequest
    {
        public TransactionEntryRequest()
        {
            Command = "transaction_entry";
        }

        /// <summary>
        /// Unique hash of the transaction you are looking up.
        /// </summary>
        [JsonPropertyName("tx_hash")]
        public string TxHash { get; set; }
    }

    /// <summary>
    /// Response expected from a <see cref="TransactionEntryRequest"/>.
    /// </summary>
    public class TransactionEntryResponse
    {
        /// <summary>
        /// The transaction object in JSON format.
        /// </summary>
        [JsonPropertyName("tx_json")]
        public JsonElement? TxJson { get; set; }

        /// <summary>
        /// The transaction metadata, which shows the exact outcome of the transaction.
        /// </summary>
        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }

        /// <summary>
        /// The ledger index of the ledger version the transaction was found in.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public uint? LedgerIndex { get; set; }

        /// <summary>
        /// The identifying hash of the ledger version the transaction was found in.
        /// </summary>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }
    }
}
