using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanManage transaction manages a loan (e.g. accept, liquidate, or modify state).
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanManage : ITransactionCommon
    {
        /// <summary>
        /// The ID of the loan to manage.
        /// </summary>
        string LoanID { get; set; }
    }

    /// <inheritdoc cref="ILoanManage" />
    public class LoanManage : TransactionRequest, ILoanManage
    {
        public LoanManage()
        {
            TransactionType = TransactionType.LoanManage;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }
    }

    /// <inheritdoc cref="ILoanManage" />
    public class LoanManageResponse : TransactionResponse, ILoanManage
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanManage(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanID", out var id) || id is not string)
                throw new ValidationException("LoanManage: missing field LoanID");
        }
    }
}
