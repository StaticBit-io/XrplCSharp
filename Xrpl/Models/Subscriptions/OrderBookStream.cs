using System.Text.Json;

using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

using JsonSerializer = System.Text.Json.JsonSerializer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/subscribe.ts

namespace Xrpl.Models.Subscriptions
{
    /// <summary>
    /// When you subscribe to one or more order books with the books field, you get back any transactions that affect those order books.
    /// <see href="https://xrpl.org/subscribe.html#order-book-streams"/>
    /// </summary>
    public class OrderBookStream : BaseStream
    {
        /// <summary>
        /// String Transaction result code
        /// </summary>
        [JsonPropertyName("engine_result")]
        public string EngineResult { get; set; }
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
        /// (Validated transactions only) The transaction metadata, which shows the exact outcome of the transaction in detail.
        /// </summary>
        [JsonPropertyName("meta")]
        public Meta Meta { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
        /// <summary>
        /// The definition of the transaction in JSON format
        /// </summary>
        [JsonPropertyName("transaction")]
        public object TransactionJson { get; set; }

        [JsonIgnore]
        public ITransactionResponse Transaction => JsonSerializer.Deserialize<TransactionResponse>(TransactionJson.ToString(), XrplJsonOptions.Default);
        /// <summary>
        /// If true, this transaction is included in a validated ledger and its outcome is final.<br/>
        /// Responses from the transaction stream should always be validated.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool Validated { get; set; }
    }
}