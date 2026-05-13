using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

// https://xrpl.org/docs/references/protocol/transactions/types/vaultcreate

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Flags for VaultCreate transactions.
    /// </summary>
    [Flags]
    public enum VaultCreateFlags : uint
    {
        /// <summary>
        /// Designates the vault as private, restricting access to credentialed accounts
        /// within a specified Permissioned Domain. Set only at vault creation.
        /// </summary>
        tfVaultPrivate = 0x00010000,

        /// <summary>
        /// Makes vault shares non-transferable between accounts. Set only at vault creation.
        /// </summary>
        tfVaultShareNonTransferable = 0x00020000,
    }

    /// <summary>
    /// The VaultCreate transaction creates a new Vault ledger object for holding pooled assets.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public interface IVaultCreate : ITransactionCommon
    {
        /// <summary>
        /// The asset held by the vault.
        /// </summary>
        IssuedCurrency Asset { get; set; }

        /// <summary>
        /// The initial deposit amount.
        /// </summary>
        Currency Amount { get; set; }

        /// <summary>
        /// The maximum asset amount that can be held in the vault.
        /// STNumber type (12 bytes: int64 mantissa + int32 exponent), serialized as string in JSON.
        /// </summary>
        string AssetsMaximum { get; set; }

        /// <summary>
        /// Arbitrary metadata for the vault shares (MPToken), limited in size. Hex-encoded string.
        /// </summary>
        string MPTokenMetadata { get; set; }

        /// <summary>
        /// The withdrawal policy for the vault. Defines how withdrawals are handled.
        /// </summary>
        uint? WithdrawalPolicy { get; set; }

        /// <summary>
        /// The scale (decimal precision) for the vault shares.
        /// </summary>
        uint? Scale { get; set; }

        /// <summary>
        /// Arbitrary hex-encoded data associated with the vault, limited to 256 bytes.
        /// </summary>
        string Data { get; set; }

        /// <summary>
        /// The ID of a permissioned domain to associate with the vault.
        /// </summary>
        string DomainID { get; set; }
    }

    /// <inheritdoc cref="IVaultCreate" />
    public class VaultCreate : TransactionRequest, IVaultCreate
    {
        public VaultCreate()
        {
            TransactionType = TransactionType.VaultCreate;
        }

        /// <inheritdoc />
        [JsonPropertyName("Asset")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AssetsMaximum")]
        public string AssetsMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenMetadata")]
        public string MPTokenMetadata { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("WithdrawalPolicy")]
        public uint? WithdrawalPolicy { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Scale")]
        public uint? Scale { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }
    }

    /// <inheritdoc cref="IVaultCreate" />
    public class VaultCreateResponse : TransactionResponse, IVaultCreate
    {
        /// <inheritdoc />
        [JsonPropertyName("Asset")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AssetsMaximum")]
        public string AssetsMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MPTokenMetadata")]
        public string MPTokenMetadata { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("WithdrawalPolicy")]
        public uint? WithdrawalPolicy { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Scale")]
        public uint? Scale { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateVaultCreate(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Asset", out var asset) || asset is null)
                throw new ValidationException("VaultCreate: missing field Asset");
        }
    }
}
