using System.Collections.Generic;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

//https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/path-and-order-book-methods/ripple_path_find
//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/ripplePathFind.ts

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// A currency that the source account might want to spend.
    /// </summary>
    public class SourceCurrency
    {
        /// <summary>
        /// Currency code (3-letter or 40-char hex).
        /// </summary>
        [JsonPropertyName("currency")]
        public string Currency { get; set; }

        /// <summary>
        /// (Optional) The issuer address for this currency.
        /// </summary>
        [JsonPropertyName("issuer")]
        public string Issuer { get; set; }
    }

    /// <summary>
    /// The ripple_path_find method is a simplified version of the path_find method
    /// that provides a single response with a payment path you can use right away.<br/>
    /// It is available in both the WebSocket and JSON-RPC APIs.<br/>
    /// Returns a <see cref="RipplePathFindResponse"/>.
    /// </summary>
    /// <code>
    /// {
    ///     "id": 8,
    ///     "command": "ripple_path_find",
    ///     "source_account": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
    ///     "source_currencies": [
    ///         { "currency": "XRP" },
    ///         { "currency": "USD" }
    ///     ],
    ///     "destination_account": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
    ///     "destination_amount": {
    ///         "value": "0.001",
    ///         "currency": "USD",
    ///         "issuer": "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"
    ///     }
    /// }
    /// </code>
    public class RipplePathFindRequest : BaseLedgerRequest
    {
        public RipplePathFindRequest(string sourceAccount, string destinationAccount, Currency destinationAmount)
        {
            Command = "ripple_path_find";
            SourceAccount = sourceAccount;
            DestinationAccount = destinationAccount;
            DestinationAmount = destinationAmount;
        }

        /// <summary>
        /// Unique address of the account that would send funds.
        /// </summary>
        [JsonPropertyName("source_account")]
        public string SourceAccount { get; set; }

        /// <summary>
        /// Unique address of the account that would receive funds.
        /// </summary>
        [JsonPropertyName("destination_account")]
        public string DestinationAccount { get; set; }

        /// <summary>
        /// Currency Amount that the destination account would receive.<br/>
        /// Special case: provide "-1" for XRP or -1 as value for tokens
        /// to request a path to deliver as much as possible,
        /// while spending no more than the amount specified in send_max.
        /// </summary>
        [JsonPropertyName("destination_amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DestinationAmount { get; set; }

        /// <summary>
        /// (Optional) Currency Amount — the maximum amount that would be spent.<br/>
        /// Cannot be used with source_currencies.
        /// </summary>
        [JsonPropertyName("send_max")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <summary>
        /// (Optional) Array of currencies that the source account might want to spend.<br/>
        /// Each entry should have a mandatory currency field and optional issuer field.<br/>
        /// Cannot contain more than 18 source currencies.<br/>
        /// By default, uses all source currencies available up to a maximum of 88 different currency/issuer pairs.
        /// </summary>
        [JsonPropertyName("source_currencies")]
        public List<SourceCurrency> SourceCurrencies { get; set; }
    }

    /// <summary>
    /// Response expected from a <see cref="RipplePathFindRequest"/>.
    /// </summary>
    public class RipplePathFindResponse
    {
        /// <summary>
        /// Array of objects with possible paths to take.<br/>
        /// If empty, then there are no paths connecting the source and destination accounts.
        /// </summary>
        [JsonPropertyName("alternatives")]
        public List<PathAlternative> Alternatives { get; set; }

        /// <summary>
        /// Unique address of the account that would receive a payment transaction.
        /// </summary>
        [JsonPropertyName("destination_account")]
        public string DestinationAccount { get; set; }

        /// <summary>
        /// Currency Amount that the destination would receive in a transaction.
        /// </summary>
        [JsonPropertyName("destination_amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DestinationAmount { get; set; }

        /// <summary>
        /// Array of strings representing the currencies that the destination accepts,
        /// as 3-letter codes like "USD" or as 40-character hex.
        /// </summary>
        [JsonPropertyName("destination_currencies")]
        public List<string> DestinationCurrencies { get; set; }

        /// <summary>
        /// If false, this is the result of an incomplete search.<br/>
        /// If true, then this is the best path found.
        /// </summary>
        [JsonPropertyName("full_reply")]
        public bool? FullReply { get; set; }

        /// <summary>
        /// Unique address of the account that would send a payment.
        /// </summary>
        [JsonPropertyName("source_account")]
        public string SourceAccount { get; set; }
    }
}
