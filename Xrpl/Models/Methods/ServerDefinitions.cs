using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/server-info-methods/server_definitions

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// The <c>server_definitions</c> method retrieves the definition enums used by the
    /// server in its binary serialization format. These include field definitions,
    /// transaction types, ledger entry types, transaction results, and type codes.
    /// </summary>
    public class ServerDefinitionsRequest : BaseRequest
    {
        public ServerDefinitionsRequest()
        {
            Command = "server_definitions";
        }

        /// <summary>
        /// (Optional) A hash value. If the hash matches the server's current definitions,
        /// the server returns an empty result, saving bandwidth.
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
    }

    /// <summary>
    /// Response expected from a <see cref="ServerDefinitionsRequest"/>.
    /// </summary>
    public class ServerDefinitionsResponse
    {
        /// <summary>
        /// An array of field definitions used in binary serialization.
        /// Each element is an array of [field_name, field_info_object].
        /// Uses JsonElement for flexibility since the structure is complex.
        /// </summary>
        [JsonPropertyName("FIELDS")]
        public JsonElement? Fields { get; set; }

        /// <summary>
        /// A mapping of ledger entry type names to their numeric codes.
        /// </summary>
        [JsonPropertyName("LEDGER_ENTRY_TYPES")]
        public Dictionary<string, int> LedgerEntryTypes { get; set; }

        /// <summary>
        /// A mapping of transaction result names to their numeric codes.
        /// </summary>
        [JsonPropertyName("TRANSACTION_RESULTS")]
        public Dictionary<string, int> TransactionResults { get; set; }

        /// <summary>
        /// A mapping of transaction type names to their numeric codes.
        /// </summary>
        [JsonPropertyName("TRANSACTION_TYPES")]
        public Dictionary<string, int> TransactionTypes { get; set; }

        /// <summary>
        /// A mapping of serialization type names to their numeric codes.
        /// </summary>
        [JsonPropertyName("TYPES")]
        public Dictionary<string, int> Types { get; set; }

        /// <summary>
        /// A hash value representing the current definitions.
        /// Can be passed in a subsequent request to avoid re-downloading unchanged definitions.
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
    }
}
