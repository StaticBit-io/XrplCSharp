using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanBrokerCoverDeposit transaction deposits cover assets into a loan broker.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanBrokerCoverDeposit : ITransactionCommon
    {
        /// <summary>
        /// The ID of the loan broker to deposit cover assets into.
        /// </summary>
        string LoanBrokerID { get; set; }

        /// <summary>
        /// The amount of cover assets to deposit.
        /// </summary>
        Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerCoverDeposit" />
    public class LoanBrokerCoverDeposit : TransactionRequest, ILoanBrokerCoverDeposit
    {
        public LoanBrokerCoverDeposit()
        {
            TransactionType = TransactionType.LoanBrokerCoverDeposit;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerCoverDeposit" />
    public class LoanBrokerCoverDepositResponse : TransactionResponse, ILoanBrokerCoverDeposit
    {
        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanBrokerCoverDeposit(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanBrokerID", out var id) || id is not string)
                throw new ValidationException("LoanBrokerCoverDeposit: missing field LoanBrokerID");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("LoanBrokerCoverDeposit: missing field Amount");
        }
    }
}
