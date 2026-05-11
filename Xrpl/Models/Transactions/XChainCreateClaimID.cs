using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchaincreateclaimid

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The XChainCreateClaimID transaction creates a new cross-chain claim ID
    /// that is used for a cross-chain transfer. A cross-chain claim ID represents
    /// one cross-chain transfer of value.
    /// </summary>
    public interface IXChainCreateClaimID : ITransactionCommon
    {
        /// <summary>
        /// The bridge to create the claim ID for.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The amount, in XRP, to reward the witness servers for providing signatures.
        /// This must match the amount on the Bridge ledger object.
        /// </summary>
        Currency SignatureReward { get; set; }

        /// <summary>
        /// The account that must send the corresponding XChainCommit on the source chain.
        /// </summary>
        string OtherChainSource { get; set; }
    }

    /// <inheritdoc cref="IXChainCreateClaimID" />
    public class XChainCreateClaimID : TransactionRequest, IXChainCreateClaimID
    {
        public XChainCreateClaimID()
        {
            TransactionType = TransactionType.XChainCreateClaimID;
        }

        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SignatureReward")]
        public Currency SignatureReward { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OtherChainSource")]
        public string OtherChainSource { get; set; }
    }

    /// <inheritdoc cref="IXChainCreateClaimID" />
    public class XChainCreateClaimIDResponse : TransactionResponse, IXChainCreateClaimID
    {
        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("SignatureReward")]
        public Currency SignatureReward { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OtherChainSource")]
        public string OtherChainSource { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainCreateClaimID(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainCreateClaimID: missing field XChainBridge");

            if (!tx.TryGetValue("SignatureReward", out var reward) || reward is null)
                throw new ValidationException("XChainCreateClaimID: missing field SignatureReward");

            if (!tx.TryGetValue("OtherChainSource", out var source) || source is not string)
                throw new ValidationException("XChainCreateClaimID: missing field OtherChainSource");
        }
    }
}
