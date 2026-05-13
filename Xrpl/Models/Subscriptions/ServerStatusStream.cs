using System.Text.Json.Serialization;

// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/subscription-methods/subscribe#server-stream

namespace Xrpl.Models.Subscriptions
{
    /// <summary>
    /// The server stream sends a message whenever the status of the rippled server changes.
    /// <see href="https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/subscription-methods/subscribe#server-stream"/>
    /// </summary>
    public class ServerStatusStream : BaseStream
    {
        /// <summary>
        /// The baseline amount of server load used in transaction cost calculations.
        /// </summary>
        [JsonPropertyName("load_base")]
        public uint LoadBase { get; set; }

        /// <summary>
        /// The load factor the server is currently enforcing. The ratio between this value
        /// and the load_base determines the multiplier for transaction costs.
        /// </summary>
        [JsonPropertyName("load_factor")]
        public uint LoadFactor { get; set; }

        /// <summary>
        /// (May be omitted) The current multiplier to the transaction cost based on
        /// load to the server due to fee escalation.
        /// </summary>
        [JsonPropertyName("load_factor_fee_escalation")]
        public uint? LoadFactorFeeEscalation { get; set; }

        /// <summary>
        /// (May be omitted) The current multiplier to the transaction cost being
        /// charged to the open ledger based on the open ledger cost.
        /// </summary>
        [JsonPropertyName("load_factor_fee_queue")]
        public uint? LoadFactorFeeQueue { get; set; }

        /// <summary>
        /// (May be omitted) The reference load factor for transaction cost calculation.
        /// </summary>
        [JsonPropertyName("load_factor_fee_reference")]
        public uint? LoadFactorFeeReference { get; set; }

        /// <summary>
        /// (May be omitted) The load factor the server is enforcing, not including
        /// the open ledger cost.
        /// </summary>
        [JsonPropertyName("load_factor_server")]
        public uint? LoadFactorServer { get; set; }

        /// <summary>
        /// A string indicating the server's current status. Possible values include
        /// "connected", "syncing", "tracking", "full", "validating", and "proposing".
        /// </summary>
        [JsonPropertyName("server_status")]
        public string ServerStatus { get; set; }

        /// <summary>
        /// (May be omitted) The minimum base fee, in drops of XRP.
        /// </summary>
        [JsonPropertyName("base_fee")]
        public uint? BaseFee { get; set; }

        /// <summary>
        /// (May be omitted) The minimum account reserve, as of the last validated ledger.
        /// </summary>
        [JsonPropertyName("reserve_base")]
        public uint? ReserveBase { get; set; }

        /// <summary>
        /// (May be omitted) The owner reserve per object, as of the last validated ledger.
        /// </summary>
        [JsonPropertyName("reserve_inc")]
        public uint? ReserveInc { get; set; }
    }
}
