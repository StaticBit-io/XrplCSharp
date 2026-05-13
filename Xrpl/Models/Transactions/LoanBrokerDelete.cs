using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanBrokerDelete transaction deletes a loan broker.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanBrokerDelete : ITransactionCommon
    {
        /// <summary>
        /// The ID of the loan broker to delete.
        /// </summary>
        string LoanBrokerID { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerDelete" />
    public class LoanBrokerDelete : TransactionRequest, ILoanBrokerDelete
    {
        public LoanBrokerDelete()
        {
            TransactionType = TransactionType.LoanBrokerDelete;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerDelete" />
    public class LoanBrokerDeleteResponse : TransactionResponse, ILoanBrokerDelete
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanBrokerDelete(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanBrokerID", out var id) || id is not string)
                throw new ValidationException("LoanBrokerDelete: missing field LoanBrokerID");
        }
    }
}
