using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/vaultset

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The VaultSet transaction modifies the settings of an existing vault.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public interface IVaultSet : ITransactionCommon
    {
        /// <summary>
        /// The ID of the vault to modify.
        /// </summary>
        string VaultID { get; set; }

        /// <summary>
        /// Arbitrary hex-encoded data associated with the vault, limited to 256 bytes.
        /// </summary>
        string Data { get; set; }

        /// <summary>
        /// The maximum asset amount that can be held in the vault.
        /// STNumber type (12 bytes: int64 mantissa + int32 exponent), serialized as string in JSON.
        /// </summary>
        string AssetsMaximum { get; set; }

        /// <summary>
        /// The ID of a permissioned domain to associate with the vault.
        /// </summary>
        string DomainID { get; set; }
    }

    /// <inheritdoc cref="IVaultSet" />
    public class VaultSet : TransactionRequest, IVaultSet
    {
        public VaultSet()
        {
            TransactionType = TransactionType.VaultSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AssetsMaximum")]
        public string AssetsMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }
    }

    /// <inheritdoc cref="IVaultSet" />
    public class VaultSetResponse : TransactionResponse, IVaultSet
    {
        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AssetsMaximum")]
        public string AssetsMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateVaultSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("VaultSet: missing field VaultID");
        }
    }
}
