using System;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Transactions
{

    /// <summary>
    /// This information is added to Transactions in request responses, but is not part  of the canonical Transaction information on ledger.<br/>
    /// These fields are denoted with  lowercase letters to indicate this in the rippled responses.
    /// </summary>
    public interface IBaseTransactionResponse
    {
        /// <summary>
        /// The date/time when this transaction was included in a validated ledger.
        /// </summary>
        [JsonConverter(typeof(RippleDateTimeConverter))]
        [JsonPropertyName("date")]
        DateTime? Date { get; set; }

        /// <summary>
        /// An identifying hash value unique to this transaction, as a hex string.
        /// </summary>
        [JsonPropertyName("hash")]
        string Hash { get; set; }

        [JsonPropertyName("inLedger")]
        uint? InLedger { get; set; }

        /// <summary>
        /// The sequence number of the ledger that included this transaction.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        uint? LedgerIndex { get; set; }

        [JsonPropertyName("validated")]
        bool? Validated { get; set; }
    }

    /// <inheritdoc />
    public class BaseTransactionResponse : IBaseTransactionResponse
    {
        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        [JsonPropertyName("date")]
        public DateTime? Date { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("inLedger")]
        public uint? InLedger { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ledger_index")]
        public uint? LedgerIndex { get; set; }

        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }
    }
}
