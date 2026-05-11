using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/vaultwithdraw

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The VaultWithdraw transaction withdraws assets from a vault.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public interface IVaultWithdraw : ITransactionCommon
    {
        /// <summary>
        /// The ID of the vault to withdraw from.
        /// </summary>
        string VaultID { get; set; }

        /// <summary>
        /// The amount to withdraw.
        /// </summary>
        Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IVaultWithdraw" />
    public class VaultWithdraw : TransactionRequest, IVaultWithdraw
    {
        public VaultWithdraw()
        {
            TransactionType = TransactionType.VaultWithdraw;
        }

        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IVaultWithdraw" />
    public class VaultWithdrawResponse : TransactionResponse, IVaultWithdraw
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
        public static async Task ValidateVaultWithdraw(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("VaultWithdraw: missing field VaultID");

            if (!tx.TryGetValue("Amount", out var amount) || amount is null)
                throw new ValidationException("VaultWithdraw: missing field Amount");
        }
    }
}
