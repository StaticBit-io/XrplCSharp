using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A LoanBroker ledger object represents a loan broker that manages lending pools and cover deposits.
/// </summary>
/// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
public class LOLoanBroker : BaseLedgerEntry
{
    public LOLoanBroker()
    {
        LedgerEntryType = LedgerEntryType.LoanBroker;
    }

    /// <summary>
    /// The account that owns this loan broker.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The primary asset managed by the loan broker.
    /// </summary>
    [JsonPropertyName("Asset")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency Asset { get; init; }

    /// <summary>
    /// The secondary asset (collateral) managed by the loan broker.
    /// </summary>
    [JsonPropertyName("Asset2")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency Asset2 { get; init; }

    /// <summary>
    /// The total cover assets available (Number type, string representation).
    /// </summary>
    [JsonPropertyName("CoverAvailable")]
    public string CoverAvailable { get; init; }

    /// <summary>
    /// The total assets available for lending (Number type, string representation).
    /// </summary>
    [JsonPropertyName("AssetsAvailable")]
    public string AssetsAvailable { get; init; }

    /// <summary>
    /// The total assets in the broker (Number type, string representation).
    /// </summary>
    [JsonPropertyName("AssetsTotal")]
    public string AssetsTotal { get; init; }

    /// <summary>
    /// The total outstanding debt (Number type, string representation).
    /// </summary>
    [JsonPropertyName("DebtTotal")]
    public string DebtTotal { get; init; }

    /// <summary>
    /// The maximum debt capacity (Number type, string representation).
    /// </summary>
    [JsonPropertyName("DebtMaximum")]
    public string DebtMaximum { get; init; }

    /// <summary>
    /// The minimum cover rate required for loans.
    /// </summary>
    [JsonPropertyName("CoverRateMinimum")]
    public uint? CoverRateMinimum { get; init; }

    /// <summary>
    /// The cover rate at which liquidation occurs.
    /// </summary>
    [JsonPropertyName("CoverRateLiquidation")]
    public uint? CoverRateLiquidation { get; init; }

    /// <summary>
    /// The management fee rate charged by the broker.
    /// </summary>
    [JsonPropertyName("ManagementFeeRate")]
    public ushort? ManagementFeeRate { get; init; }

    /// <summary>
    /// A hint linking to the loan broker's directory node.
    /// </summary>
    [JsonPropertyName("LoanBrokerNode")]
    public string LoanBrokerNode { get; init; }

    /// <summary>
    /// The ID of a permissioned domain associated with the broker.
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
