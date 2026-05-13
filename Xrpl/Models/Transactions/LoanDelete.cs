using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanDelete transaction deletes a loan.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanDelete : ITransactionCommon
    {
        /// <summary>
        /// The ID of the loan to delete.
        /// </summary>
        string LoanID { get; set; }
    }

    /// <inheritdoc cref="ILoanDelete" />
    public class LoanDelete : TransactionRequest, ILoanDelete
    {
        public LoanDelete()
        {
            TransactionType = TransactionType.LoanDelete;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }
    }

    /// <inheritdoc cref="ILoanDelete" />
    public class LoanDeleteResponse : TransactionResponse, ILoanDelete
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanDelete(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanID", out var id) || id is not string)
                throw new ValidationException("LoanDelete: missing field LoanID");
        }
    }
}
