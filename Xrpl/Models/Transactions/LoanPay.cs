using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Flags for the LoanPay transaction. Mutually exclusive.
    /// </summary>
    [Flags]
    public enum LoanPayFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,

        /// <summary>
        /// Pay more than the scheduled amount (overpayment).
        /// </summary>
        tfLoanOverpayment = 0x00010000,

        /// <summary>
        /// Pay the full remaining balance of the loan.
        /// </summary>
        tfLoanFullPayment = 0x00020000,

        /// <summary>
        /// Make a late payment on the loan.
        /// </summary>
        tfLoanLatePayment = 0x00040000,
    }

    /// <summary>
    /// The LoanPay transaction makes a payment on a loan.
    /// Flags are mutually exclusive.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanPay : ITransactionCommon
    {
        /// <summary>
        /// LoanPay transaction flags.
        /// </summary>
        new LoanPayFlags? Flags { get; set; }

        /// <summary>
        /// The ID of the loan to make a payment on.
        /// </summary>
        string LoanID { get; set; }

        /// <summary>
        /// The amount to pay.
        /// </summary>
        Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanPay" />
    public class LoanPay : TransactionRequest, ILoanPay
    {
        public LoanPay()
        {
            TransactionType = TransactionType.LoanPay;
        }

        /// <inheritdoc />
        public new LoanPayFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanPayFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanPay" />
    public class LoanPayResponse : TransactionResponse, ILoanPay
    {
        /// <inheritdoc />
        public new LoanPayFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanPayFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanPay(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanID", out var id) || id is not string)
                throw new ValidationException("LoanPay: missing field LoanID");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("LoanPay: missing field Amount");
        }
    }
}
