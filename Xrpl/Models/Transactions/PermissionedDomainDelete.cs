using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/permissioneddomaindelete

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Delete a permissioned domain that you own.
    /// Requires the PermissionedDomains amendment.
    /// </summary>
    public interface IPermissionedDomainDelete : ITransactionCommon
    {
        /// <summary>
        /// The ledger entry ID of the Permissioned Domain entry to delete.
        /// </summary>
        string DomainID { get; set; }
    }

    /// <inheritdoc cref="IPermissionedDomainDelete" />
    public class PermissionedDomainDelete : TransactionRequest, IPermissionedDomainDelete
    {
        /// <summary>
        /// Initializes a new instance of the PermissionedDomainDelete class.
        /// </summary>
        public PermissionedDomainDelete()
        {
            TransactionType = TransactionType.PermissionedDomainDelete;
        }

        /// <inheritdoc />
        [JsonProperty("DomainID")]
        public string DomainID { get; set; }
    }

    /// <summary>
    /// Response for a PermissionedDomainDelete transaction.
    /// </summary>
    public class PermissionedDomainDeleteResponse : TransactionResponse, IPermissionedDomainDelete
    {
        /// <inheritdoc />
        [JsonProperty("DomainID")]
        public string DomainID { get; set; }
    }

    /// <summary>
    /// Validation for PermissionedDomainDelete transactions.
    /// </summary>
    public partial class Validation
    {
        /// <summary>
        /// Validates a PermissionedDomainDelete transaction.
        /// </summary>
        /// <param name="tx">A PermissionedDomainDelete transaction.</param>
        /// <exception cref="ValidationException">When the PermissionedDomainDelete is malformed.</exception>
        public static async Task ValidatePermissionedDomainDelete(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue(nameof(IPermissionedDomainDelete.DomainID), out var domainId) || domainId == null || (domainId is not string domainIdStr || string.IsNullOrEmpty(domainIdStr)))
            {
                throw new ValidationException("PermissionedDomainDelete: DomainID is required");
            }
        }
    }
}
