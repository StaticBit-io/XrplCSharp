using System;
using System.Text.Json.Serialization;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// Flags of a Loan ledger object.
/// </summary>
[Flags]
public enum LoanFlags : uint
{
    /// <summary>
    /// The loan is in default: the borrower missed a payment past the grace period.
    /// </summary>
    lsfLoanDefault = 0x00010000,

    /// <summary>
    /// The loan is impaired: the broker expects it not to be repaid in full.
    /// </summary>
    lsfLoanImpaired = 0x00020000,

    /// <summary>
    /// The loan allows overpayments.
    /// </summary>
    lsfLoanOverpayment = 0x00040000,
}

/// <summary>
/// A Loan ledger object represents a loan between a borrower and a loan broker.
/// </summary>
/// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
public class LOLoan : BaseLedgerEntry
{
    /// <summary>
    /// A bit-map of boolean flags enabled for this loan.
    /// </summary>
    [JsonPropertyName("Flags")]
    public LoanFlags? Flags { get; init; }

    /// <summary>
    /// The account address of the Borrower.
    /// </summary>
    [JsonPropertyName("Borrower")]
    public string Borrower { get; init; }

    /// <summary>
    /// The ID of the Loan Broker associated with this loan.
    /// </summary>
    [JsonPropertyName("LoanBrokerID")]
    public string LoanBrokerID { get; init; }

    /// <summary>
    /// The loan's sequence number.
    /// </summary>
    [JsonPropertyName("LoanSequence")]
    public uint? LoanSequence { get; init; }

    /// <summary>
    /// The annualized interest rate for the loan, in 1/10th of a basis point: 100000 = 100% (0–100000).
    /// </summary>
    [JsonPropertyName("InterestRate")]
    public uint? InterestRate { get; init; }

    /// <summary>
    /// The premium added to the interest rate for late payments, in 1/10th of a basis point: 100000 = 100% (0–100000).
    /// </summary>
    [JsonPropertyName("LateInterestRate")]
    public uint? LateInterestRate { get; init; }

    /// <summary>
    /// The rate charged for repaying the loan early, in 1/10th of a basis point: 100000 = 100% (0–100000).
    /// </summary>
    [JsonPropertyName("CloseInterestRate")]
    public uint? CloseInterestRate { get; init; }

    /// <summary>
    /// The interest rate charged on overpayments, in 1/10th of a basis point: 100000 = 100% (0–100000).
    /// </summary>
    [JsonPropertyName("OverpaymentInterestRate")]
    public uint? OverpaymentInterestRate { get; init; }

    /// <summary>
    /// The fee rate charged on overpayments, in 1/10th of a basis point: 100000 = 100% (0–100000).
    /// </summary>
    [JsonPropertyName("OverpaymentFee")]
    public uint? OverpaymentFee { get; init; }

    /// <summary>
    /// The remaining principal owed (Number type).
    /// </summary>
    [JsonPropertyName("PrincipalOutstanding")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? PrincipalOutstanding { get; init; }

    /// <summary>
    /// The total amount owed including fees (Number type).
    /// </summary>
    [JsonPropertyName("TotalValueOutstanding")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? TotalValueOutstanding { get; init; }

    /// <summary>
    /// The amount due per payment interval (Number type).
    /// </summary>
    [JsonPropertyName("PeriodicPayment")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? PeriodicPayment { get; init; }

    /// <summary>
    /// The remaining management fee to broker (Number type).
    /// </summary>
    [JsonPropertyName("ManagementFeeOutstanding")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? ManagementFeeOutstanding { get; init; }

    /// <summary>
    /// The fee paid to broker at loan creation (Number type).
    /// </summary>
    [JsonPropertyName("LoanOriginationFee")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? LoanOriginationFee { get; init; }

    /// <summary>
    /// The fee paid to broker with each payment (Number type).
    /// </summary>
    [JsonPropertyName("LoanServiceFee")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? LoanServiceFee { get; init; }

    /// <summary>
    /// The fee for late payments (Number type).
    /// </summary>
    [JsonPropertyName("LatePaymentFee")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? LatePaymentFee { get; init; }

    /// <summary>
    /// The fee for early full repayment (Number type).
    /// </summary>
    [JsonPropertyName("ClosePaymentFee")]
    [JsonConverter(typeof(LenientXrplNumberConverter))]
    public XrplNumber? ClosePaymentFee { get; init; }

    /// <summary>
    /// The timestamp of when the loan started, in seconds since the Ripple Epoch.
    /// </summary>
    [JsonPropertyName("StartDate")]
    [JsonConverter(typeof(RippleDateTimeConverter))]
    public DateTime? StartDate { get; init; }

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
    /// The timestamp of when the previous payment was made, in seconds since the Ripple Epoch.
    /// </summary>
    [JsonPropertyName("PreviousPaymentDueDate")]
    [JsonConverter(typeof(RippleDateTimeConverter))]
    public DateTime? PreviousPaymentDueDate { get; init; }

    /// <summary>
    /// The timestamp of when the next payment is due, in seconds since the Ripple Epoch.
    /// </summary>
    [JsonPropertyName("NextPaymentDueDate")]
    [JsonConverter(typeof(RippleDateTimeConverter))]
    public DateTime? NextPaymentDueDate { get; init; }

    /// <summary>
    /// The number of payments remaining.
    /// </summary>
    [JsonPropertyName("PaymentRemaining")]
    public uint? PaymentRemaining { get; init; }

    /// <summary>
    /// The scale factor for decimal place rounding.
    /// </summary>
    [JsonPropertyName("LoanScale")]
    public int? LoanScale { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object (UInt64).
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// A hint linking to the loan broker's directory node (UInt64).
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
