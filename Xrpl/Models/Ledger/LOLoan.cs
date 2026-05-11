using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// A Loan ledger object represents a loan between a borrower and a loan broker.
/// </summary>
/// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
public class LOLoan : BaseLedgerEntry
{
    public LOLoan()
    {
        LedgerEntryType = LedgerEntryType.Loan;
    }

    /// <summary>
    /// The borrower account.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The counterparty (lender/broker) account.
    /// </summary>
    [JsonPropertyName("Counterparty")]
    public string Counterparty { get; init; }

    /// <summary>
    /// The ID of the loan broker managing this loan.
    /// </summary>
    [JsonPropertyName("LoanBrokerID")]
    public string LoanBrokerID { get; init; }

    /// <summary>
    /// The sequence number of this loan within the broker.
    /// </summary>
    [JsonPropertyName("LoanSequence")]
    public uint? LoanSequence { get; init; }

    /// <summary>
    /// The asset being loaned.
    /// </summary>
    [JsonPropertyName("Asset")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency Asset { get; init; }

    /// <summary>
    /// The interest rate for the loan.
    /// </summary>
    [JsonPropertyName("InterestRate")]
    public uint? InterestRate { get; init; }

    /// <summary>
    /// The interest rate applied for late payments.
    /// </summary>
    [JsonPropertyName("LateInterestRate")]
    public uint? LateInterestRate { get; init; }

    /// <summary>
    /// The interest rate applied when closing the loan early.
    /// </summary>
    [JsonPropertyName("CloseInterestRate")]
    public uint? CloseInterestRate { get; init; }

    /// <summary>
    /// The interest rate applied on overpayments.
    /// </summary>
    [JsonPropertyName("OverpaymentInterestRate")]
    public uint? OverpaymentInterestRate { get; init; }

    /// <summary>
    /// The fee charged for overpayments.
    /// </summary>
    [JsonPropertyName("OverpaymentFee")]
    public uint? OverpaymentFee { get; init; }

    /// <summary>
    /// The outstanding principal (Number type, string representation).
    /// </summary>
    [JsonPropertyName("PrincipalOutstanding")]
    public string PrincipalOutstanding { get; init; }

    /// <summary>
    /// The principal amount originally requested (Number type, string representation).
    /// </summary>
    [JsonPropertyName("PrincipalRequested")]
    public string PrincipalRequested { get; init; }

    /// <summary>
    /// The total value outstanding (Number type, string representation).
    /// </summary>
    [JsonPropertyName("TotalValueOutstanding")]
    public string TotalValueOutstanding { get; init; }

    /// <summary>
    /// The periodic payment amount (Number type, string representation).
    /// </summary>
    [JsonPropertyName("PeriodicPayment")]
    public string PeriodicPayment { get; init; }

    /// <summary>
    /// The outstanding management fee (Number type, string representation).
    /// </summary>
    [JsonPropertyName("ManagementFeeOutstanding")]
    public string ManagementFeeOutstanding { get; init; }

    /// <summary>
    /// The origination fee (Number type, string representation).
    /// </summary>
    [JsonPropertyName("LoanOriginationFee")]
    public string LoanOriginationFee { get; init; }

    /// <summary>
    /// The service fee (Number type, string representation).
    /// </summary>
    [JsonPropertyName("LoanServiceFee")]
    public string LoanServiceFee { get; init; }

    /// <summary>
    /// The late payment fee (Number type, string representation).
    /// </summary>
    [JsonPropertyName("LatePaymentFee")]
    public string LatePaymentFee { get; init; }

    /// <summary>
    /// The close payment fee (Number type, string representation).
    /// </summary>
    [JsonPropertyName("ClosePaymentFee")]
    public string ClosePaymentFee { get; init; }

    /// <summary>
    /// The start date of the loan (Ripple epoch timestamp).
    /// </summary>
    [JsonPropertyName("StartDate")]
    public uint? StartDate { get; init; }

    /// <summary>
    /// The interval between payments, in seconds.
    /// </summary>
    [JsonPropertyName("PaymentInterval")]
    public uint? PaymentInterval { get; init; }

    /// <summary>
    /// The grace period before late fees apply, in seconds.
    /// </summary>
    [JsonPropertyName("GracePeriod")]
    public uint? GracePeriod { get; init; }

    /// <summary>
    /// The due date of the previous payment (Ripple epoch timestamp).
    /// </summary>
    [JsonPropertyName("PreviousPaymentDueDate")]
    public uint? PreviousPaymentDueDate { get; init; }

    /// <summary>
    /// The due date of the next payment (Ripple epoch timestamp).
    /// </summary>
    [JsonPropertyName("NextPaymentDueDate")]
    public uint? NextPaymentDueDate { get; init; }

    /// <summary>
    /// The number of payments remaining.
    /// </summary>
    [JsonPropertyName("PaymentRemaining")]
    public uint? PaymentRemaining { get; init; }

    /// <summary>
    /// The total number of payments.
    /// </summary>
    [JsonPropertyName("PaymentTotal")]
    public uint? PaymentTotal { get; init; }

    /// <summary>
    /// The scale factor for loan calculations.
    /// </summary>
    [JsonPropertyName("LoanScale")]
    public int? LoanScale { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object.
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// A hint linking to the loan broker's directory node.
    /// </summary>
    [JsonPropertyName("LoanBrokerNode")]
    public string LoanBrokerNode { get; init; }

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
