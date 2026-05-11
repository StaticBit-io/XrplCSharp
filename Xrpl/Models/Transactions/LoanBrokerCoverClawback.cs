using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanBrokerCoverClawback transaction claws back cover assets from a loan broker.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanBrokerCoverClawback : ITransactionCommon
    {
        /// <summary>
        /// The ID of the loan broker to claw back from.
        /// </summary>
        string LoanBrokerID { get; set; }

        /// <summary>
        /// The holder account to claw back from.
        /// </summary>
        string Holder { get; set; }

        /// <summary>
        /// The amount to claw back.
        /// </summary>
        Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerCoverClawback" />
    public class LoanBrokerCoverClawback : TransactionRequest, ILoanBrokerCoverClawback
    {
        public LoanBrokerCoverClawback()
        {
            TransactionType = TransactionType.LoanBrokerCoverClawback;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerCoverClawback" />
    public class LoanBrokerCoverClawbackResponse : TransactionResponse, ILoanBrokerCoverClawback
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanBrokerCoverClawback(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanBrokerID", out var id) || id is not string)
                throw new ValidationException("LoanBrokerCoverClawback: missing field LoanBrokerID");

            if (!tx.TryGetValue("Holder", out var holder) || holder is not string)
                throw new ValidationException("LoanBrokerCoverClawback: missing field Holder");
        }
    }
}
