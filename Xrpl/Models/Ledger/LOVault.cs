using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A Vault ledger object represents a pooled asset vault.
/// </summary>
/// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
public class LOVault : BaseLedgerEntry
{
    public LOVault()
    {
        LedgerEntryType = LedgerEntryType.Vault;
    }

    /// <summary>
    /// The account that owns this vault.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The primary asset held by the vault.
    /// </summary>
    [JsonPropertyName("Asset")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency Asset { get; init; }

    /// <summary>
    /// A secondary asset associated with the vault.
    /// </summary>
    [JsonPropertyName("Asset2")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency Asset2 { get; init; }

    /// <summary>
    /// The total amount of assets available in the vault.
    /// </summary>
    [JsonPropertyName("AssetsAvailable")]
    public string AssetsAvailable { get; init; }

    /// <summary>
    /// The maximum amount of assets the vault can hold.
    /// </summary>
    [JsonPropertyName("AssetsMaximum")]
    public string AssetsMaximum { get; init; }

    /// <summary>
    /// The total amount of assets currently in the vault (including locked/unavailable).
    /// </summary>
    [JsonPropertyName("AssetsTotal")]
    public string AssetsTotal { get; init; }

    /// <summary>
    /// The amount of unrealized loss in the vault.
    /// </summary>
    [JsonPropertyName("LossUnrealized")]
    public string LossUnrealized { get; init; }

    /// <summary>
    /// The withdrawal policy for the vault.
    /// </summary>
    [JsonPropertyName("WithdrawalPolicy")]
    public byte? WithdrawalPolicy { get; init; }

    /// <summary>
    /// Flags that can be modified after creation.
    /// </summary>
    [JsonPropertyName("MutableFlags")]
    public uint? MutableFlags { get; init; }

    /// <summary>
    /// Arbitrary hex-encoded data associated with the vault.
    /// </summary>
    [JsonPropertyName("Data")]
    public string Data { get; init; }

    /// <summary>
    /// The ID of a permissioned domain associated with the vault.
    /// </summary>
    [JsonPropertyName("DomainID")]
    public string DomainID { get; init; }

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
