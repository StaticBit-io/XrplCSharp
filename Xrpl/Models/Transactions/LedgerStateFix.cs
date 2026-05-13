using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LedgerStateFix pseudo-transaction fixes ledger state inconsistencies.
    /// This is an administrative transaction.
    /// </summary>
    public interface ILedgerStateFix : ITransactionCommon
    {
        /// <summary>
        /// The type of ledger fix to apply.
        /// </summary>
        ushort LedgerFixType { get; set; }

        /// <summary>
        /// The owner account whose ledger objects need fixing.
        /// </summary>
        string Owner { get; set; }
    }

    /// <inheritdoc cref="ILedgerStateFix" />
    public class LedgerStateFix : TransactionRequest, ILedgerStateFix
    {
        public LedgerStateFix()
        {
            TransactionType = TransactionType.LedgerStateFix;
        }

        /// <inheritdoc />
        [JsonPropertyName("LedgerFixType")]
        public ushort LedgerFixType { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Owner")]
        public string Owner { get; set; }
    }

    /// <inheritdoc cref="ILedgerStateFix" />
    public class LedgerStateFixResponse : TransactionResponse, ILedgerStateFix
    {
        /// <inheritdoc />
        [JsonPropertyName("LedgerFixType")]
        public ushort LedgerFixType { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Owner")]
        public string Owner { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLedgerStateFix(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LedgerFixType", out var fixType) || fixType is null)
                throw new ValidationException("LedgerStateFix: missing field LedgerFixType");
        }
    }
}
