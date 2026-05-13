using System.Text.Json.Serialization;

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
    /// The address of the LoanBroker pseudo-account.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The account address of the vault owner.
    /// </summary>
    [JsonPropertyName("Owner")]
    public string Owner { get; init; }

    /// <summary>
    /// The associated vault identifier.
    /// </summary>
    [JsonPropertyName("VaultID")]
    public string VaultID { get; init; }

    /// <summary>
    /// The transaction sequence number that created the LoanBroker.
    /// </summary>
    [JsonPropertyName("Sequence")]
    public uint? Sequence { get; init; }

    /// <summary>
    /// Sequential identifier for Loan ledger entries, incremented each time a new loan is created.
    /// </summary>
    [JsonPropertyName("LoanSequence")]
    public uint? LoanSequence { get; init; }

    /// <summary>
    /// The number of active loans issued by the LoanBroker.
    /// </summary>
    [JsonPropertyName("OwnerCount")]
    public uint? OwnerCount { get; init; }

    /// <summary>
    /// Total asset amount the protocol owes the vault, including interest (Number type, string representation).
    /// </summary>
    [JsonPropertyName("DebtTotal")]
    public string DebtTotal { get; init; }

    /// <summary>
    /// Protocol debt ceiling; 0 indicates unlimited (Number type, string representation).
    /// </summary>
    [JsonPropertyName("DebtMaximum")]
    public string DebtMaximum { get; init; }

    /// <summary>
    /// Total amount of first-loss capital deposited (Number type, string representation).
    /// </summary>
    [JsonPropertyName("CoverAvailable")]
    public string CoverAvailable { get; init; }

    /// <summary>
    /// Minimum first-loss capital coverage ratio, in 1/10th basis points.
    /// </summary>
    [JsonPropertyName("CoverRateMinimum")]
    public uint? CoverRateMinimum { get; init; }

    /// <summary>
    /// Minimum required first-loss capital moved to cover loan default.
    /// </summary>
    [JsonPropertyName("CoverRateLiquidation")]
    public uint? CoverRateLiquidation { get; init; }

    /// <summary>
    /// Protocol fee in 1/10th basis points (0-100000).
    /// </summary>
    [JsonPropertyName("ManagementFeeRate")]
    public ushort? ManagementFeeRate { get; init; }

    /// <summary>
    /// Arbitrary metadata about the loan broker, limited to 256 bytes. Hex-encoded string.
    /// </summary>
    [JsonPropertyName("Data")]
    public string Data { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object (UInt64).
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// Reference page in vault's pseudo-account directory (UInt64).
    /// </summary>
    [JsonPropertyName("VaultNode")]
    public string VaultNode { get; init; }

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
