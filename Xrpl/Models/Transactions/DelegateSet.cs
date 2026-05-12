using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The DelegateSet transaction grants permissions to another account to send
    /// transactions on your behalf.
    /// </summary>
    public interface IDelegateSet : ITransactionCommon
    {
        /// <summary>
        /// The account to delegate permissions to.
        /// </summary>
        string Delegate { get; set; }

        /// <summary>
        /// An array of permission objects defining which transaction types the delegate can submit.
        /// </summary>
        List<PermissionWrapper> Permissions { get; set; }
    }

    /// <inheritdoc cref="IDelegateSet" />
    public class DelegateSet : TransactionRequest, IDelegateSet
    {
        public DelegateSet()
        {
            TransactionType = TransactionType.DelegateSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("Delegate")]
        public string Delegate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Permissions")]
        public List<PermissionWrapper> Permissions { get; set; }
    }

    /// <inheritdoc cref="IDelegateSet" />
    public class DelegateSetResponse : TransactionResponse, IDelegateSet
    {
        /// <inheritdoc />
        [JsonPropertyName("Delegate")]
        public string Delegate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Permissions")]
        public List<PermissionWrapper> Permissions { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateDelegateSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Delegate", out var auth) || auth is not string)
                throw new ValidationException("DelegateSet: missing field Delegate");

            if (!tx.TryGetValue("Permissions", out var perms) || perms is null)
                throw new ValidationException("DelegateSet: missing field Permissions");
        }
    }
}
