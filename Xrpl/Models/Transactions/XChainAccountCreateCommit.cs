using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchainaccountcreatecommit

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The XChainAccountCreateCommit transaction creates a new account for a witness's
    /// account on one of the chains a bridge connects. This transaction can only be used
    /// for XRP-XRP bridges.
    /// </summary>
    public interface IXChainAccountCreateCommit : ITransactionCommon
    {
        /// <summary>
        /// The bridge to create accounts for.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The destination account on the destination chain.
        /// </summary>
        string Destination { get; set; }

        /// <summary>
        /// The amount, in XRP, to use for account creation. This must be greater than
        /// or equal to the MinAccountCreateAmount specified in the Bridge ledger object.
        /// </summary>
        Currency Amount { get; set; }

        /// <summary>
        /// The amount, in XRP, to be used to reward the witness servers for providing
        /// signatures. This must match the amount on the Bridge ledger object.
        /// </summary>
        Currency SignatureReward { get; set; }
    }

    /// <inheritdoc cref="IXChainAccountCreateCommit" />
    public class XChainAccountCreateCommit : TransactionRequest, IXChainAccountCreateCommit
    {
        public XChainAccountCreateCommit()
        {
            TransactionType = TransactionType.XChainAccountCreateCommit;
        }

        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SignatureReward")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SignatureReward { get; set; }
    }

    /// <inheritdoc cref="IXChainAccountCreateCommit" />
    public class XChainAccountCreateCommitResponse : TransactionResponse, IXChainAccountCreateCommit
    {
        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SignatureReward")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency SignatureReward { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainAccountCreateCommit(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainAccountCreateCommit: missing field XChainBridge");

            if (!tx.TryGetValue("Destination", out var dest) || dest is not string)
                throw new ValidationException("XChainAccountCreateCommit: missing field Destination");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("XChainAccountCreateCommit: missing field Amount");

            if (!tx.TryGetValue("SignatureReward", out var reward) || reward is null)
                throw new ValidationException("XChainAccountCreateCommit: missing field SignatureReward");
        }
    }
}
