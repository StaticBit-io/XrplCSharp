using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchaincommit

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The XChainCommit transaction is the second step in a cross-chain transfer.
    /// It puts assets into trust on the locking chain so that they can be wrapped
    /// on the issuing chain, or burns wrapped assets on the issuing chain so that
    /// they can be returned on the locking chain.
    /// </summary>
    public interface IXChainCommit : ITransactionCommon
    {
        /// <summary>
        /// The bridge to use for the transfer.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The unique integer ID for the cross-chain claim.
        /// </summary>
        string XChainClaimID { get; set; }

        /// <summary>
        /// The amount to transfer.
        /// </summary>
        Currency Amount { get; set; }

        /// <summary>
        /// The destination account on the destination chain.
        /// If this is not specified, the account that submitted the XChainCreateClaimID
        /// transaction on the destination chain will need to submit an XChainClaim transaction.
        /// </summary>
        string OtherChainDestination { get; set; }
    }

    /// <inheritdoc cref="IXChainCommit" />
    public class XChainCommit : TransactionRequest, IXChainCommit
    {
        public XChainCommit()
        {
            TransactionType = TransactionType.XChainCommit;
        }

        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("XChainClaimID")]
        public string XChainClaimID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OtherChainDestination")]
        public string OtherChainDestination { get; set; }
    }

    /// <inheritdoc cref="IXChainCommit" />
    public class XChainCommitResponse : TransactionResponse, IXChainCommit
    {
        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("XChainClaimID")]
        public string XChainClaimID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OtherChainDestination")]
        public string OtherChainDestination { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainCommit(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainCommit: missing field XChainBridge");

            if (!tx.TryGetValue("XChainClaimID", out var claimId) || claimId is null)
                throw new ValidationException("XChainCommit: missing field XChainClaimID");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("XChainCommit: missing field Amount");
        }
    }
}
