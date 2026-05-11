using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/vaultdelete

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The VaultDelete transaction deletes an empty vault.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public interface IVaultDelete : ITransactionCommon
    {
        /// <summary>
        /// The ID of the vault to delete.
        /// </summary>
        string VaultID { get; set; }
    }

    /// <inheritdoc cref="IVaultDelete" />
    public class VaultDelete : TransactionRequest, IVaultDelete
    {
        public VaultDelete()
        {
            TransactionType = TransactionType.VaultDelete;
        }

        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }
    }

    /// <inheritdoc cref="IVaultDelete" />
    public class VaultDeleteResponse : TransactionResponse, IVaultDelete
    {
        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateVaultDelete(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("VaultDelete: missing field VaultID");
        }
    }
}
