using System.Text.Json.Serialization;

namespace Xrpl.Models.Common;

/// <summary>
/// An attestation element within an XChainClaimAttestations array on an XChainOwnedClaimID ledger object.
/// </summary>
public class XChainClaimAttestationCollectionElement
{
    /// <summary>
    /// The inner attestation data.
    /// </summary>
    [JsonPropertyName("XChainClaimProofSig")]
    public XChainClaimProofSig XChainClaimProofSig { get; set; }
}

/// <summary>
/// The proof signature data for a cross-chain claim attestation.
/// </summary>
public class XChainClaimProofSig
{
    /// <summary>
    /// The amount to claim in the XChainCommit transaction on the destination chain.
    /// </summary>
    [JsonPropertyName("Amount")]
    public Currency Amount { get; set; }

    /// <summary>
    /// The account that should receive this signer's share of the SignatureReward.
    /// </summary>
    [JsonPropertyName("AttestationRewardAccount")]
    public string AttestationRewardAccount { get; set; }

    /// <summary>
    /// The account on the door account's signer list that is signing the transaction.
    /// </summary>
    [JsonPropertyName("AttestationSignerAccount")]
    public string AttestationSignerAccount { get; set; }

    /// <summary>
    /// The destination account for the funds on the destination chain.
    /// </summary>
    [JsonPropertyName("Destination")]
    public string Destination { get; set; }

    /// <summary>
    /// The public key used to verify the signature.
    /// </summary>
    [JsonPropertyName("PublicKey")]
    public string PublicKey { get; set; }

    /// <summary>
    /// A boolean representing the chain where the event occurred (1 = locking chain, 0 = issuing chain).
    /// </summary>
    [JsonPropertyName("WasLockingChainSend")]
    public byte WasLockingChainSend { get; set; }
}

/// <summary>
/// An attestation element within an XChainCreateAccountAttestations array.
/// </summary>
public class XChainCreateAccountAttestationCollectionElement
{
    /// <summary>
    /// The inner attestation data.
    /// </summary>
    [JsonPropertyName("XChainCreateAccountProofSig")]
    public XChainCreateAccountProofSig XChainCreateAccountProofSig { get; set; }
}

/// <summary>
/// The proof signature data for a cross-chain account create attestation.
/// </summary>
public class XChainCreateAccountProofSig
{
    /// <summary>
    /// The amount for the account creation.
    /// </summary>
    [JsonPropertyName("Amount")]
    public Currency Amount { get; set; }

    /// <summary>
    /// The signature reward for the attestation.
    /// </summary>
    [JsonPropertyName("SignatureReward")]
    public Currency SignatureReward { get; set; }

    /// <summary>
    /// The account that should receive this signer's share of the SignatureReward.
    /// </summary>
    [JsonPropertyName("AttestationRewardAccount")]
    public string AttestationRewardAccount { get; set; }

    /// <summary>
    /// The account on the door account's signer list that is signing the transaction.
    /// </summary>
    [JsonPropertyName("AttestationSignerAccount")]
    public string AttestationSignerAccount { get; set; }

    /// <summary>
    /// The destination account for the newly created account.
    /// </summary>
    [JsonPropertyName("Destination")]
    public string Destination { get; set; }

    /// <summary>
    /// The public key used to verify the signature.
    /// </summary>
    [JsonPropertyName("PublicKey")]
    public string PublicKey { get; set; }

    /// <summary>
    /// A boolean representing the chain where the event occurred (1 = locking chain, 0 = issuing chain).
    /// </summary>
    [JsonPropertyName("WasLockingChainSend")]
    public byte WasLockingChainSend { get; set; }
}
