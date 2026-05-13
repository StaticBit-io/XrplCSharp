using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Models.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// An XChainOwnedCreateAccountClaimID ledger object represents one cross-chain
/// account create transaction and collects attestations for it.
/// </summary>
public class LOXChainOwnedCreateAccountClaimID : BaseLedgerEntry
{
    public LOXChainOwnedCreateAccountClaimID()
    {
        LedgerEntryType = LedgerEntryType.XChainOwnedCreateAccountClaimID;
    }

    /// <summary>
    /// The account that owns this object.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The bridge specification.
    /// </summary>
    [JsonPropertyName("XChainBridge")]
    public XChainBridgeModel XChainBridge { get; init; }

    /// <summary>
    /// An integer that determines the order that accounts created through
    /// cross-chain transfers must be performed.
    /// </summary>
    [JsonPropertyName("XChainAccountCreateCount")]
    public string XChainAccountCreateCount { get; init; }

    /// <summary>
    /// Attestations collected from the witness servers for the account creation.
    /// </summary>
    [JsonPropertyName("XChainCreateAccountAttestations")]
    public List<XChainCreateAccountAttestationCollectionElement> XChainCreateAccountAttestations { get; init; }

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
