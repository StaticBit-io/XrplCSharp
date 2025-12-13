using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using System;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transactions;

namespace Xrpl.Models.Ledger
{
    [JsonConverter(typeof(TransactionOrHashConverter))]
    public class HashOrTransaction
    {
        /// <summary>
        /// Unique hash of the transaction you are looking up
        /// </summary>
        public string TransactionHash { get; set; }
        /// <summary>
        /// server transaction response
        /// </summary>
        public LedgerTransaction Transaction { get; set; }
    }

    public class LedgerTransaction 
    {
        /// <summary>
        /// The ledger close time represented in ISO 8601 time format.
        /// </summary>
        [JsonProperty("close_time_iso")]
        [JsonConverter(typeof(IsoDateTimeConverter))]
        public DateTime CloseTimeIso { get; set; }

        /// <summary>
        /// A hex string of the ledger version that included this transaction.
        /// </summary>
        [JsonProperty("ledger_hash")]
        public string LedgerHash { get; set; }
        /// <summary>
        /// The ledger index of the ledger version that included this transaction.
        /// </summary>
        [JsonProperty("ledger_index")]
        public ulong? LedgerIndex { get; set; }
        /// <summary>
        /// If binary is True, then this is a hex string of the transaction metadata.<br/>
        /// Otherwise, the transaction metadata is included in JSON format.
        /// </summary>
        [JsonProperty("meta")]
        [JsonConverter(typeof(MetaBinaryConverter))]
        public Meta Meta { get; set; }
        /// <summary>
        /// JSON object defining the transaction.
        /// </summary>
        [JsonProperty("tx_json")]
        [JsonConverter(typeof(TransactionRequestConverter))]

        public ITransactionRequest Transaction { get; set; }

        /// <summary>
        /// Unique hashed String representing the transaction.
        /// </summary>
        [JsonProperty("hash")]
        public string Hash { get; set; }
        /// <summary>
        /// Whether or not the transaction is included in a validated ledger.<br/>
        /// Any transaction not yet in a validated ledger is subject to change.
        /// </summary>
        [JsonProperty("validated")]
        public bool Validated { get; set; }
    }

}
