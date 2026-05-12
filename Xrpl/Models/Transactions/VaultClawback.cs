using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/vaultclawback

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The VaultClawback transaction allows an issuer to claw back assets from a vault.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public interface IVaultClawback : ITransactionCommon
    {
        /// <summary>
        /// The ID of the vault to claw back from.
        /// </summary>
        string VaultID { get; set; }

        /// <summary>
        /// The amount to claw back. If omitted, claws back all available assets.
        /// </summary>
        Currency Amount { get; set; }

        /// <summary>
        /// The holder account to claw back from.
        /// </summary>
        string Holder { get; set; }
    }

    /// <inheritdoc cref="IVaultClawback" />
    public class VaultClawback : TransactionRequest, IVaultClawback
    {
        public VaultClawback()
        {
            TransactionType = TransactionType.VaultClawback;
        }

        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }
    }

    /// <inheritdoc cref="IVaultClawback" />
    public class VaultClawbackResponse : TransactionResponse, IVaultClawback
    {
        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateVaultClawback(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("VaultClawback: missing field VaultID");

            if (!tx.TryGetValue("Holder", out var holder) || holder is not string)
                throw new ValidationException("VaultClawback: missing field Holder");
        }
    }
}
