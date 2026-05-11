using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchaincreatebridge

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The XChainCreateBridge transaction creates a new Bridge ledger object
    /// and defines a cross-chain bridge between a locking chain and an issuing chain.
    /// </summary>
    public interface IXChainCreateBridge : ITransactionCommon
    {
        /// <summary>
        /// The bridge (door accounts and brided assets) to create.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The signature reward split between the witnesses for submitting attestations.
        /// </summary>
        Currency SignatureReward { get; set; }

        /// <summary>
        /// The minimum amount, in XRP, required for an XChainAccountCreateCommit transaction.
        /// If this is not present, the XChainAccountCreateCommit transaction will fail.
        /// </summary>
        Currency MinAccountCreateAmount { get; set; }
    }

    /// <inheritdoc cref="IXChainCreateBridge" />
    public class XChainCreateBridge : TransactionRequest, IXChainCreateBridge
    {
        public XChainCreateBridge()
        {
            TransactionType = TransactionType.XChainCreateBridge;
        }

        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SignatureReward")]
        public Currency SignatureReward { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MinAccountCreateAmount")]
        public Currency MinAccountCreateAmount { get; set; }
    }

    /// <inheritdoc cref="IXChainCreateBridge" />
    public class XChainCreateBridgeResponse : TransactionResponse, IXChainCreateBridge
    {
        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SignatureReward")]
        public Currency SignatureReward { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MinAccountCreateAmount")]
        public Currency MinAccountCreateAmount { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainCreateBridge(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainCreateBridge: missing field XChainBridge");

            if (!tx.TryGetValue("SignatureReward", out var reward) || reward is null)
                throw new ValidationException("XChainCreateBridge: missing field SignatureReward");
        }
    }
}
