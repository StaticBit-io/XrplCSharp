using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A Sponsorship ledger object records a sponsorship relationship between a sponsor
/// (Owner) and a sponsee: which fees and reserves the sponsor covers.
/// </summary>
/// <remarks>Requires the Sponsor amendment (XLS-68).</remarks>
public class LOSponsorship : BaseLedgerEntry
{
    public LOSponsorship()
    {
        LedgerEntryType = LedgerEntryType.Sponsorship;
    }

    /// <summary>
    /// The sponsoring account.
    /// </summary>
    [JsonPropertyName("Owner")]
    public string Owner { get; init; }

    /// <summary>
    /// The sponsored account.
    /// </summary>
    [JsonPropertyName("Sponsee")]
    public string Sponsee { get; init; }

    /// <summary>
    /// The remaining amount of fees the sponsor covers.
    /// </summary>
    [JsonPropertyName("FeeAmount")]
    [JsonConverter(typeof(CurrencyConverter))]
    public Currency FeeAmount { get; init; }

    /// <summary>
    /// The maximum fee per transaction the sponsor covers.
    /// </summary>
    [JsonPropertyName("MaxFee")]
    [JsonConverter(typeof(CurrencyConverter))]
    public Currency MaxFee { get; init; }

    /// <summary>
    /// The remaining number of owner-reserve slots the sponsor covers.
    /// </summary>
    [JsonPropertyName("RemainingOwnerCount")]
    public uint? RemainingOwnerCount { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object.
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// A hint indicating which page of the sponsee's directory links to this object.
    /// </summary>
    [JsonPropertyName("SponseeNode")]
    public string SponseeNode { get; init; }

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
