using System.Collections.Generic;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/pathFind.ts
//https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/methods/subscribe.ts#L382

namespace Xrpl.Models.Subscriptions
{
    /// <summary>
    /// Asynchronous follow-up message from the server for an ongoing path_find request.<br/>
    /// These messages have "type": "path_find" and include the id of the original WebSocket request.
    /// </summary>
    public class PathFindStream : BaseStream
    {
        /// <summary>
        /// Unique address that would send a transaction.
        /// </summary>
        [JsonPropertyName("source_account")]
        public string SourceAccount { get; set; }

        /// <summary>
        /// Unique address of the account that would receive a transaction.
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
        /// If false, this is the result of an incomplete search. A later reply may have a better path.<br/>
        /// If true, then this is the best path found.<br/>
        /// Until you close the pathfinding request, rippled continues to send updates each time a new ledger closes.
        /// </summary>
        [JsonPropertyName("full_reply")]
        public bool FullReply { get; set; }

        /// <summary>
        /// The ID provided in the WebSocket request is included again at this level.
        /// </summary>
        [JsonPropertyName("id")]
        public object Id { get; set; }

        /// <summary>
        /// (Optional) Currency Amount that would be spent in the transaction.
        /// </summary>
        [JsonPropertyName("send_max")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SendMax { get; set; }

        /// <summary>
        /// Array of objects with suggested paths to take.<br/>
        /// If empty, then no paths were found connecting the source and destination accounts.
        /// </summary>
        [JsonPropertyName("alternatives")]
        public List<PathAlternative> Alternatives { get; set; }
    }
}
