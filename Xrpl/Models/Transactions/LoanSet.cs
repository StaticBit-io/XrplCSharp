using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanSet transaction creates or modifies a loan associated with a loan broker.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanSet : ITransactionCommon
    {
        /// <summary>
        /// The ID of the loan broker managing this loan.
        /// </summary>
        string LoanBrokerID { get; set; }

        /// <summary>
        /// The borrower account.
        /// </summary>
        string Borrower { get; set; }

        /// <summary>
        /// The asset being loaned.
        /// </summary>
        IssuedCurrency Asset { get; set; }

        /// <summary>
        /// The interest rate for the loan, as a percentage multiplied by 1000.
        /// </summary>
        uint? InterestRate { get; set; }

        /// <summary>
        /// The interest rate applied for late payments.
        /// </summary>
        uint? LateInterestRate { get; set; }

        /// <summary>
        /// The interest rate applied when closing the loan early.
        /// </summary>
        uint? CloseInterestRate { get; set; }

        /// <summary>
        /// The interest rate applied on overpayments.
        /// </summary>
        uint? OverpaymentInterestRate { get; set; }

        /// <summary>
        /// The fee charged for overpayments.
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
        /// The principal amount requested (Number type, string representation).
        /// </summary>
        string PrincipalRequested { get; set; }

        /// <summary>
        /// The start date of the loan (Ripple epoch timestamp).
        /// </summary>
        uint? StartDate { get; set; }

        /// <summary>
        /// The interval between payments, in seconds.
        /// </summary>
        uint? PaymentInterval { get; set; }

        /// <summary>
        /// The grace period before late fees apply, in seconds.
        /// </summary>
        uint? GracePeriod { get; set; }

        /// <summary>
        /// The total number of payments for the loan.
        /// </summary>
        uint? PaymentTotal { get; set; }

        /// <summary>
        /// The scale factor for loan calculations.
        /// </summary>
        int? LoanScale { get; set; }
    }

    /// <inheritdoc cref="ILoanSet" />
    public class LoanSet : TransactionRequest, ILoanSet
    {
        public LoanSet()
        {
            TransactionType = TransactionType.LoanSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Borrower")]
        public string Borrower { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Asset")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

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
        [JsonPropertyName("PrincipalRequested")]
        public string PrincipalRequested { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("StartDate")]
        public uint? StartDate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentInterval")]
        public uint? PaymentInterval { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("GracePeriod")]
        public uint? GracePeriod { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentTotal")]
        public uint? PaymentTotal { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanScale")]
        public int? LoanScale { get; set; }
    }

    /// <inheritdoc cref="ILoanSet" />
    public class LoanSetResponse : TransactionResponse, ILoanSet
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Borrower")]
        public string Borrower { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Asset")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

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
        [JsonPropertyName("PrincipalRequested")]
        public string PrincipalRequested { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("StartDate")]
        public uint? StartDate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentInterval")]
        public uint? PaymentInterval { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("GracePeriod")]
        public uint? GracePeriod { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("PaymentTotal")]
        public uint? PaymentTotal { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanScale")]
        public int? LoanScale { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanBrokerID", out var brokerId) || brokerId is not string)
                throw new ValidationException("LoanSet: missing field LoanBrokerID");

            if (!tx.TryGetValue("Borrower", out var borrower) || borrower is not string)
                throw new ValidationException("LoanSet: missing field Borrower");

            if (!tx.TryGetValue("Asset", out var asset) || asset is null)
                throw new ValidationException("LoanSet: missing field Asset");
        }
    }
}
