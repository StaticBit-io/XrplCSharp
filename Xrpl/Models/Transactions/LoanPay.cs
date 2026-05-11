using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanPay transaction makes a payment on a loan.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanPay : ITransactionCommon
    {
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
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanPay" />
    public class LoanPayResponse : TransactionResponse, ILoanPay
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
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
