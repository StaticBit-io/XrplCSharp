using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Enums;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Flags for the LoanSet transaction.
    /// </summary>
    [Flags]
    public enum LoanSetFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,

        /// <summary>
        /// Enables overpayment on the loan, allowing the borrower to pay more than the scheduled amount.
        /// Sets lsfLoanOverpayment on the Loan ledger object.
        /// </summary>
        tfLoanOverpayment = 0x00010000,
    }

    /// <summary>
    /// The LoanSet transaction creates a new loan associated with a loan broker.
    /// The borrower (Counterparty) must co-sign via CounterpartySignature unless
    /// this transaction is part of a Batch.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanSet : ITransactionCommon
    {
        /// <summary>
        /// LoanSet transaction flags.
        /// </summary>
        new LoanSetFlags? Flags { get; set; }

        /// <summary>
        /// The ID of the loan broker managing this loan.
        /// </summary>
        string LoanBrokerID { get; set; }

        /// <summary>
        /// The borrower account (counterparty to the loan).
        /// </summary>
        string Counterparty { get; set; }

        /// <summary>
        /// The principal amount requested for the loan (Number type, string representation).
        /// Required.
        /// </summary>
        string PrincipalRequested { get; set; }

        /// <summary>
        /// The interest rate for the loan (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? InterestRate { get; set; }

        /// <summary>
        /// The interest rate applied for late payments (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? LateInterestRate { get; set; }

        /// <summary>
        /// The interest rate applied when closing the loan early (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? CloseInterestRate { get; set; }

        /// <summary>
        /// The interest rate applied on overpayments (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? OverpaymentInterestRate { get; set; }

        /// <summary>
        /// The fee charged for overpayments (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? OverpaymentFee { get; set; }

        /// <summary>
        /// The origination fee for the loan (Number type, string representation).
        /// </summary>
        string LoanOriginationFee { get; set; }

        /// <summary>
        /// The service fee for the loan (Number type, string representation).
        /// </summary>
        string LoanServiceFee { get; set; }

        /// <summary>
        /// The fee charged for late payments (Number type, string representation).
        /// </summary>
        string LatePaymentFee { get; set; }

        /// <summary>
        /// The fee charged for early loan closure (Number type, string representation).
        /// </summary>
        string ClosePaymentFee { get; set; }

        /// <summary>
        /// The total number of payments for the loan. Default: 1.
        /// </summary>
        uint? PaymentTotal { get; set; }

        /// <summary>
        /// The interval between payments, in seconds. Default: 60.
        /// </summary>
        uint? PaymentInterval { get; set; }

        /// <summary>
        /// The grace period before late fees apply, in seconds. Default: 60.
        /// </summary>
        uint? GracePeriod { get; set; }

        /// <summary>
        /// Arbitrary hex-encoded metadata, limited to 256 bytes.
        /// </summary>
        string Data { get; set; }
    }

    /// <inheritdoc cref="ILoanSet" />
    public class LoanSet : TransactionRequest, ILoanSet
    {
        public LoanSet()
        {
            TransactionType = TransactionType.LoanSet;
        }

        /// <inheritdoc />
        public new LoanSetFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Counterparty")]
        public string Counterparty { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PrincipalRequested")]
        public string PrincipalRequested { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("InterestRate")]
        public uint? InterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LateInterestRate")]
        public uint? LateInterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CloseInterestRate")]
        public uint? CloseInterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OverpaymentInterestRate")]
        public uint? OverpaymentInterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OverpaymentFee")]
        public uint? OverpaymentFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanOriginationFee")]
        public string LoanOriginationFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanServiceFee")]
        public string LoanServiceFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LatePaymentFee")]
        public string LatePaymentFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ClosePaymentFee")]
        public string ClosePaymentFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentTotal")]
        public uint? PaymentTotal { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentInterval")]
        public uint? PaymentInterval { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("GracePeriod")]
        public uint? GracePeriod { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }
    }

    /// <inheritdoc cref="ILoanSet" />
    public class LoanSetResponse : TransactionResponse, ILoanSet
    {
        /// <inheritdoc />
        public new LoanSetFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Counterparty")]
        public string Counterparty { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PrincipalRequested")]
        public string PrincipalRequested { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("InterestRate")]
        public uint? InterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LateInterestRate")]
        public uint? LateInterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CloseInterestRate")]
        public uint? CloseInterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OverpaymentInterestRate")]
        public uint? OverpaymentInterestRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("OverpaymentFee")]
        public uint? OverpaymentFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanOriginationFee")]
        public string LoanOriginationFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanServiceFee")]
        public string LoanServiceFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LatePaymentFee")]
        public string LatePaymentFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ClosePaymentFee")]
        public string ClosePaymentFee { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentTotal")]
        public uint? PaymentTotal { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentInterval")]
        public uint? PaymentInterval { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("GracePeriod")]
        public uint? GracePeriod { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanBrokerID", out var brokerId) || brokerId is not string)
                throw new ValidationException("LoanSet: missing field LoanBrokerID");

            if (!tx.TryGetValue("PrincipalRequested", out var principal) || principal is null)
                throw new ValidationException("LoanSet: missing field PrincipalRequested");
        }
    }
}
