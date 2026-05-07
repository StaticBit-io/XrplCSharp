using System.Collections.Generic;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

//https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/path-and-order-book-methods/path_find
//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/pathFind.ts

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Each element in the alternatives array represents a path from one possible
    /// source currency to the destination account and currency.
    /// </summary>
    public class PathAlternative
    {
        /// <summary>
        /// Array of arrays of objects defining payment paths.
        /// </summary>
        [JsonPropertyName("paths_computed")]
        public List<List<Path>> PathsComputed { get; set; }

        /// <summary>
        /// (Deprecated) Array of arrays of objects defining canonical payment paths.<br/>
        /// May be present in server responses but should be disregarded.
        /// </summary>
        [JsonPropertyName("paths_canonical")]
        public List<List<Path>> PathsCanonical { get; set; }

        /// <summary>
        /// Currency Amount that the source would have to send along this path
        /// for the destination to receive the desired amount.
        /// </summary>
        [JsonPropertyName("source_amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SourceAmount { get; set; }

        /// <summary>
        /// (May be omitted) Currency Amount that the destination would receive along this path.<br/>
        /// Only included when the destination_amount from the request was the "-1" special case.
        /// </summary>
        [JsonPropertyName("destination_amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DestinationAmount { get; set; }
    }

    /// <summary>
    /// Response expected from a path_find create, close, or status request.
    /// </summary>
    public class PathFindResponse
    {
        /// <summary>
        /// Array of objects with suggested paths to take.<br/>
        /// If empty, then no paths were found connecting the source and destination accounts.
        /// </summary>
        [JsonPropertyName("alternatives")]
        public List<PathAlternative> Alternatives { get; set; }

        /// <summary>
        /// Unique address of the account that would receive a payment.
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
        /// Unique address of the account that would send a payment.
        /// </summary>
        [JsonPropertyName("source_account")]
        public string SourceAccount { get; set; }

        /// <summary>
        /// If false, this is the result of an incomplete search.<br/>
        /// If true, then this is the best path found.
        /// </summary>
        [JsonPropertyName("full_reply")]
        public bool FullReply { get; set; }

        /// <summary>
        /// (path_find close only) The value true indicates this reply is in response to a path_find close command.
        /// </summary>
        [JsonPropertyName("closed")]
        public bool? Closed { get; set; }
    }

    /// <summary>
    /// The path_find create sub-command creates an ongoing request to find possible paths
    /// along which a payment transaction could be made from one specified account such that
    /// another account receives a desired amount of some currency.<br/>
    /// WebSocket API only.
    /// </summary>
    /// <code>
    /// {
    ///     "id": 1,
    ///     "command": "path_find",
    ///     "subcommand": "create",
    ///     "source_account": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
    ///     "destination_account": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
    ///     "destination_amount": {
    ///         "value": "0.001",
    ///         "currency": "USD",
    ///         "issuer": "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"
    ///     }
    /// }
    /// </code>
    public class PathFindCreateRequest : BaseRequest
    {
        public PathFindCreateRequest(string sourceAccount, string destinationAccount, Currency destinationAmount)
        {
            Command = "path_find";
            SubCommand = "create";
            SourceAccount = sourceAccount;
            DestinationAccount = destinationAccount;
            DestinationAmount = destinationAmount;
        }

        /// <summary>
        /// Use "create" to send the create sub-command.
        /// </summary>
        [JsonPropertyName("subcommand")]
        public string SubCommand { get; set; }

        /// <summary>
        /// Unique address of the account to find a path from (the sender).
        /// </summary>
        [JsonPropertyName("source_account")]
        public string SourceAccount { get; set; }

        /// <summary>
        /// Unique address of the account to find a path to (the receiver).
        /// </summary>
        [JsonPropertyName("destination_account")]
        public string DestinationAccount { get; set; }

        /// <summary>
        /// Currency Amount that the destination account would receive.<br/>
        /// Special case: provide -1 as the value to request a path to deliver as much as possible,
        /// while spending no more than the amount specified in send_max.
        /// </summary>
        [JsonPropertyName("destination_amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency DestinationAmount { get; set; }

        /// <summary>
        /// (Optional) Currency Amount — the maximum amount that would be spent.<br/>
        /// Not compatible with source_currencies.
        /// </summary>
        [JsonPropertyName("send_max")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <summary>
        /// (Optional) Array of arrays of objects, representing payment paths to check.<br/>
        /// You can use this to keep updated on changes to particular paths you already know about,
        /// or to check the overall cost to make a payment along a certain path.
        /// </summary>
        [JsonPropertyName("paths")]
        public List<List<Path>> Paths { get; set; }
    }

    /// <summary>
    /// The path_find close sub-command instructs the server to stop sending information
    /// about the current open pathfinding request.<br/>
    /// WebSocket API only.
    /// </summary>
    /// <code>
    /// {
    ///     "id": 57,
    ///     "command": "path_find",
    ///     "subcommand": "close"
    /// }
    /// </code>
    public class PathFindCloseRequest : BaseRequest
    {
        public PathFindCloseRequest()
        {
            Command = "path_find";
            SubCommand = "close";
        }

        /// <summary>
        /// Use "close" to send the close sub-command.
        /// </summary>
        [JsonPropertyName("subcommand")]
        public string SubCommand { get; set; }
    }

    /// <summary>
    /// The path_find status sub-command requests an immediate update about the client's
    /// currently-open pathfinding request.<br/>
    /// WebSocket API only.
    /// </summary>
    /// <code>
    /// {
    ///     "id": 58,
    ///     "command": "path_find",
    ///     "subcommand": "status"
    /// }
    /// </code>
    public class PathFindStatusRequest : BaseRequest
    {
        public PathFindStatusRequest()
        {
            Command = "path_find";
            SubCommand = "status";
        }

        /// <summary>
        /// Use "status" to send the status sub-command.
        /// </summary>
        [JsonPropertyName("subcommand")]
        public string SubCommand { get; set; }
    }
}
