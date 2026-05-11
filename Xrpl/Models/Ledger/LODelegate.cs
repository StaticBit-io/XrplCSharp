using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Models.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A Delegate ledger object records which permissions have been granted
/// by one account to another.
/// </summary>
public class LODelegate : BaseLedgerEntry
{
    public LODelegate()
    {
        LedgerEntryType = LedgerEntryType.Delegate;
    }

    /// <summary>
    /// The account that granted the permissions.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The account that received the permissions.
    /// </summary>
    [JsonPropertyName("Delegate")]
    public string Delegate { get; init; }

    /// <summary>
    /// The permissions granted to the delegate.
    /// </summary>
    [JsonPropertyName("Permissions")]
    public List<PermissionWrapper> Permissions { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object.
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
