using System.Collections.Generic;
using System.Threading.Tasks;

using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/oracledelete

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Deletes an Oracle ledger entry. Only the owner of the price oracle can send this transaction.
    /// </summary>
    public interface IOracleDelete : ITransactionCommon
    {
        /// <summary>
        /// A unique identifier of the price oracle for the Account.
        /// </summary>
        uint OracleDocumentID { get; set; }
    }

    /// <inheritdoc cref="IOracleDelete" />
    public class OracleDelete : TransactionRequest, IOracleDelete
    {
        /// <summary>
        /// Initializes a new instance of the OracleDelete class.
        /// </summary>
        public OracleDelete()
        {
            TransactionType = TransactionType.OracleDelete;
        }

        /// <inheritdoc />
        [JsonPropertyName("OracleDocumentID")]
        public uint OracleDocumentID { get; set; }
    }

    /// <inheritdoc cref="IOracleDelete" />
    public class OracleDeleteResponse : TransactionResponse, IOracleDelete
    {
        /// <inheritdoc />
        [JsonPropertyName("OracleDocumentID")]
        public uint OracleDocumentID { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of an OracleDelete at runtime.
        /// </summary>
        /// <param name="tx">An OracleDelete Transaction.</param>
        /// <exception cref="ValidationException">When the OracleDelete is malformed.</exception>
        public static async Task ValidateOracleDelete(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("OracleDocumentID", out var oracleDocumentID) || oracleDocumentID is null)
                throw new ValidationException("OracleDelete: missing field OracleDocumentID");
        }
    }
}
