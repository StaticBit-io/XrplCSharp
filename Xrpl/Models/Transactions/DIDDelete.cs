using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/diddelete

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Deletes the DID (Decentralized Identifier) associated with the sending account.
    /// </summary>
    public interface IDIDDelete : ITransactionCommon
    {
    }

    /// <inheritdoc cref="IDIDDelete" />
    public class DIDDelete : TransactionRequest, IDIDDelete
    {
        /// <summary>
        /// Initializes a new instance of the DIDDelete class.
        /// </summary>
        public DIDDelete()
        {
            TransactionType = TransactionType.DIDDelete;
        }
    }

    /// <inheritdoc cref="IDIDDelete" />
    public class DIDDeleteResponse : TransactionResponse, IDIDDelete
    {
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a DIDDelete at runtime.
        /// </summary>
        /// <param name="tx">A DIDDelete Transaction.</param>
        /// <exception cref="ValidationException">When the DIDDelete is malformed.</exception>
        public static async Task ValidateDIDDelete(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);
        }
    }
}
