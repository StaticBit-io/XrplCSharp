using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchainclaim

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The XChainClaim transaction completes a cross-chain transfer of value.
    /// It allows a user to claim the value on the destination chain — the
    /// equivalent of the value locked on the source chain.
    /// </summary>
    public interface IXChainClaim : ITransactionCommon
    {
        /// <summary>
        /// The bridge to use for the claim.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The unique integer ID for the cross-chain claim.
        /// </summary>
        string XChainClaimID { get; set; }

        /// <summary>
        /// The destination account on the destination chain. It must exist or the transaction will fail.
        /// </summary>
        string Destination { get; set; }

        /// <summary>
        /// An integer destination tag.
        /// </summary>
        uint? DestinationTag { get; set; }

        /// <summary>
        /// The amount to claim on the destination chain. This must match the amount
        /// attested to on the attestations associated with this claim ID.
        /// </summary>
        Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IXChainClaim" />
    public class XChainClaim : TransactionRequest, IXChainClaim
    {
        public XChainClaim()
        {
            TransactionType = TransactionType.XChainClaim;
        }

        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("XChainClaimID")]
        public string XChainClaimID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DestinationTag")]
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IXChainClaim" />
    public class XChainClaimResponse : TransactionResponse, IXChainClaim
    {
        /// <inheritdoc />
        [JsonPropertyName("XChainBridge")]
        public XChainBridgeModel XChainBridge { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("XChainClaimID")]
        public string XChainClaimID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DestinationTag")]
        public uint? DestinationTag { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainClaim(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainClaim: missing field XChainBridge");

            if (!tx.TryGetValue("XChainClaimID", out var claimId) || claimId is null)
                throw new ValidationException("XChainClaim: missing field XChainClaimID");

            if (!tx.TryGetValue("Destination", out var dest) || dest is not string)
                throw new ValidationException("XChainClaim: missing field Destination");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("XChainClaim: missing field Amount");
        }
    }
}
