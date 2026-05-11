using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchainmodifybridge

namespace Xrpl.Models.Transactions
{
    [Flags]
    public enum XChainModifyBridgeFlags : uint
    {
        /// <summary>
        /// Clears the MinAccountCreateAmount of the bridge.
        /// </summary>
        tfClearAccountCreateAmount = 0x00010000,
    }

    /// <summary>
    /// The XChainModifyBridge transaction allows bridge managers to modify
    /// the parameters of an existing bridge.
    /// </summary>
    public interface IXChainModifyBridge : ITransactionCommon
    {
        /// <summary>
        /// The bridge to modify.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The signature reward split between the witnesses for submitting attestations.
        /// </summary>
        Currency SignatureReward { get; set; }

        /// <summary>
        /// The minimum amount, in XRP, required for an XChainAccountCreateCommit transaction.
        /// </summary>
        Currency MinAccountCreateAmount { get; set; }

        new XChainModifyBridgeFlags? Flags { get; set; }
    }

    /// <inheritdoc cref="IXChainModifyBridge" />
    public class XChainModifyBridge : TransactionRequest, IXChainModifyBridge
    {
        public XChainModifyBridge()
        {
            TransactionType = TransactionType.XChainModifyBridge;
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

        public new XChainModifyBridgeFlags? Flags
        {
            get => base.Flags.HasValue ? (XChainModifyBridgeFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    /// <inheritdoc cref="IXChainModifyBridge" />
    public class XChainModifyBridgeResponse : TransactionResponse, IXChainModifyBridge
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

        public new XChainModifyBridgeFlags? Flags
        {
            get => base.Flags.HasValue ? (XChainModifyBridgeFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainModifyBridge(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainModifyBridge: missing field XChainBridge");
        }
    }
}
