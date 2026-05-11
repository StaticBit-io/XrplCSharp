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
        /// A secondary asset associated with the vault.
        /// </summary>
        IssuedCurrency Asset2 { get; set; }

        /// <summary>
        /// The initial deposit amount.
        /// </summary>
        Currency Amount { get; set; }

        /// <summary>
        /// The withdrawal policy for the vault. Defines how withdrawals are handled.
        /// </summary>
        byte? WithdrawalPolicy { get; set; }

        /// <summary>
        /// Flags that can be modified after creation.
        /// </summary>
        uint? MutableFlags { get; set; }

        /// <summary>
        /// Arbitrary hex-encoded data associated with the vault.
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
        [JsonPropertyName("Asset2")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset2 { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("WithdrawalPolicy")]
        public byte? WithdrawalPolicy { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MutableFlags")]
        public uint? MutableFlags { get; set; }

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
        [JsonPropertyName("Asset2")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset2 { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Amount")]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("WithdrawalPolicy")]
        public byte? WithdrawalPolicy { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("MutableFlags")]
        public uint? MutableFlags { get; set; }

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
