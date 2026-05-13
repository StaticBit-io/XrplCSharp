using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/xchainaddclaimattestation

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The XChainAddClaimAttestation transaction provides proof from a witness server,
    /// attesting to an XChainCommit transaction.
    /// </summary>
    public interface IXChainAddClaimAttestation : ITransactionCommon
    {
        /// <summary>
        /// The bridge to attest to.
        /// </summary>
        XChainBridgeModel XChainBridge { get; set; }

        /// <summary>
        /// The XChainClaimID associated with the transfer, which was included in the XChainCommit transaction.
        /// </summary>
        string XChainClaimID { get; set; }

        /// <summary>
        /// The amount committed by the XChainCommit transaction on the source chain.
        /// </summary>
        Currency Amount { get; set; }

        /// <summary>
        /// The account that should receive this signer's share of the SignatureReward.
        /// </summary>
        string AttestationRewardAccount { get; set; }

        /// <summary>
        /// The account on the door account's signer list that is signing the transaction.
        /// </summary>
        string AttestationSignerAccount { get; set; }

        /// <summary>
        /// The destination account for the funds on the destination chain.
        /// </summary>
        string Destination { get; set; }

        /// <summary>
        /// The account on the source chain that submitted the XChainCommit transaction
        /// that triggered the event associated with the attestation.
        /// </summary>
        string OtherChainSource { get; set; }

        /// <summary>
        /// The public key used to verify the signature.
        /// </summary>
        string PublicKey { get; set; }

        /// <summary>
        /// The signature attesting to the event on the other chain.
        /// </summary>
        string Signature { get; set; }

        /// <summary>
        /// A boolean representing the chain where the event occurred.
        /// 0 represents the issuing chain, 1 represents the locking chain.
        /// </summary>
        byte WasLockingChainSend { get; set; }
    }

    /// <inheritdoc cref="IXChainAddClaimAttestation" />
    public class XChainAddClaimAttestation : TransactionRequest, IXChainAddClaimAttestation
    {
        public XChainAddClaimAttestation()
        {
            TransactionType = TransactionType.XChainAddClaimAttestation;
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
        [JsonPropertyName("AttestationRewardAccount")]
        public string AttestationRewardAccount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AttestationSignerAccount")]
        public string AttestationSignerAccount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OtherChainSource")]
        public string OtherChainSource { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PublicKey")]
        public string PublicKey { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Signature")]
        public string Signature { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("WasLockingChainSend")]
        public byte WasLockingChainSend { get; set; }
    }

    /// <inheritdoc cref="IXChainAddClaimAttestation" />
    public class XChainAddClaimAttestationResponse : TransactionResponse, IXChainAddClaimAttestation
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
        [JsonPropertyName("AttestationRewardAccount")]
        public string AttestationRewardAccount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AttestationSignerAccount")]
        public string AttestationSignerAccount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Destination")]
        public string Destination { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OtherChainSource")]
        public string OtherChainSource { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PublicKey")]
        public string PublicKey { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Signature")]
        public string Signature { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("WasLockingChainSend")]
        public byte WasLockingChainSend { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateXChainAddClaimAttestation(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("XChainBridge", out var bridge) || bridge is null)
                throw new ValidationException("XChainAddClaimAttestation: missing field XChainBridge");

            if (!tx.TryGetValue("XChainClaimID", out var claimId) || claimId is null)
                throw new ValidationException("XChainAddClaimAttestation: missing field XChainClaimID");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("XChainAddClaimAttestation: missing field Amount");

            if (!tx.TryGetValue("AttestationRewardAccount", out var rewardAccount) || rewardAccount is not string)
                throw new ValidationException("XChainAddClaimAttestation: missing field AttestationRewardAccount");

            if (!tx.TryGetValue("AttestationSignerAccount", out var signerAccount) || signerAccount is not string)
                throw new ValidationException("XChainAddClaimAttestation: missing field AttestationSignerAccount");

            if (!tx.TryGetValue("OtherChainSource", out var source) || source is not string)
                throw new ValidationException("XChainAddClaimAttestation: missing field OtherChainSource");

            if (!tx.TryGetValue("PublicKey", out var pk) || pk is not string)
                throw new ValidationException("XChainAddClaimAttestation: missing field PublicKey");

            if (!tx.TryGetValue("Signature", out var sig) || sig is not string)
                throw new ValidationException("XChainAddClaimAttestation: missing field Signature");

            if (!tx.TryGetValue("WasLockingChainSend", out var wasLocking) || wasLocking is null)
                throw new ValidationException("XChainAddClaimAttestation: missing field WasLockingChainSend");
        }
    }
}
