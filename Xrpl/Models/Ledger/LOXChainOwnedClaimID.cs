using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// An XChainOwnedClaimID ledger object represents one cross-chain transfer of value
/// and includes information of the value locked on the source chain.
/// </summary>
public class LOXChainOwnedClaimID : BaseLedgerEntry
{
    public LOXChainOwnedClaimID()
    {
        LedgerEntryType = LedgerEntryType.XChainOwnedClaimID;
    }

    /// <summary>
    /// The account that owns this claim ID.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The bridge specification.
    /// </summary>
    [JsonPropertyName("XChainBridge")]
    public XChainBridgeModel XChainBridge { get; init; }

    /// <summary>
    /// The unique sequence number for the cross-chain claim.
    /// </summary>
    [JsonPropertyName("XChainClaimID")]
    public string XChainClaimID { get; init; }

    /// <summary>
    /// The account that must send the corresponding XChainCommit on the source chain.
    /// </summary>
    [JsonPropertyName("OtherChainSource")]
    public string OtherChainSource { get; init; }

    /// <summary>
    /// The total amount to be rewarded for providing attestation signatures.
    /// </summary>
    [JsonPropertyName("SignatureReward")]
    [JsonConverter(typeof(CurrencyConverter))]
    public Currency SignatureReward { get; init; }

    /// <summary>
    /// Attestations collected from the witness servers.
    /// </summary>
    [JsonPropertyName("XChainClaimAttestations")]
    public List<XChainClaimAttestationCollectionElement> XChainClaimAttestations { get; init; }

    /// <summary>
    /// A hint indicating which page of the sender's owner directory links to this object.
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// The identifying hash of the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnID")]
    public string PreviousTxnID { get; init; }

    /// <summary>
    /// The index of the ledger that contains the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnLgrSeq")]
    public uint? PreviousTxnLgrSeq { get; init; }
}
