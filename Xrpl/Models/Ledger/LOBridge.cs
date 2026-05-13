using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A Bridge ledger object represents a single cross-chain bridge that connects
/// and enables value to move efficiently between two blockchains.
/// </summary>
public class LOBridge : BaseLedgerEntry
{
    public LOBridge()
    {
        LedgerEntryType = LedgerEntryType.Bridge;
    }

    /// <summary>
    /// The account that owns this bridge on this chain.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The bridge specification (door accounts and issued currencies).
    /// </summary>
    [JsonPropertyName("XChainBridge")]
    public XChainBridgeModel XChainBridge { get; init; }

    /// <summary>
    /// The total amount, in XRP, to be rewarded for providing a signature for cross-chain transfer or account creation.
    /// </summary>
    [JsonPropertyName("SignatureReward")]
    [JsonConverter(typeof(CurrencyConverter))]
    public Currency SignatureReward { get; init; }

    /// <summary>
    /// The minimum amount, in XRP, required for an XChainAccountCreateCommit transaction.
    /// </summary>
    [JsonPropertyName("MinAccountCreateAmount")]
    [JsonConverter(typeof(CurrencyConverter))]
    public Currency MinAccountCreateAmount { get; init; }

    /// <summary>
    /// A counter used to order the creation of new claim IDs.
    /// </summary>
    [JsonPropertyName("XChainClaimID")]
    public string XChainClaimID { get; init; }

    /// <summary>
    /// A counter used to order the creation of new accounts.
    /// </summary>
    [JsonPropertyName("XChainAccountCreateCount")]
    public string XChainAccountCreateCount { get; init; }

    /// <summary>
    /// A counter representing the next account create count expected to be claimed.
    /// </summary>
    [JsonPropertyName("XChainAccountClaimCount")]
    public string XChainAccountClaimCount { get; init; }

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
