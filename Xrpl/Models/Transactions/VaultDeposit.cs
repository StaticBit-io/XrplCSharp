using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/vaultdeposit

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The VaultDeposit transaction deposits assets into a vault.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public interface IVaultDeposit : ITransactionCommon
    {
        /// <summary>
        /// The ID of the vault to deposit into.
        /// </summary>
        string VaultID { get; set; }

        /// <summary>
        /// The amount to deposit.
        /// </summary>
        Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IVaultDeposit" />
    public class VaultDeposit : TransactionRequest, IVaultDeposit
    {
        public VaultDeposit()
        {
            TransactionType = TransactionType.VaultDeposit;
        }

        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IVaultDeposit" />
    public class VaultDepositResponse : TransactionResponse, IVaultDeposit
    {
        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateVaultDeposit(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("VaultDeposit: missing field VaultID");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("VaultDeposit: missing field Amount");
        }
    }
}
