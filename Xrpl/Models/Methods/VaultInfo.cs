using System.Text.Json.Serialization;

using Xrpl.Models.Ledger;

// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/path-and-order-book-methods/vault_info

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// The <c>vault_info</c> method retrieves information about a vault.
    /// </summary>
    /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
    public class VaultInfoRequest : BaseLedgerRequest
    {
        public VaultInfoRequest()
        {
            Command = "vault_info";
        }

        /// <summary>
        /// The unique identifier (Hash256) of the vault to look up.
        /// </summary>
        [JsonPropertyName("vault_id")]
        public string VaultID { get; set; }
    }

    /// <summary>
    /// Response expected from a <see cref="VaultInfoRequest"/>.
    /// </summary>
    public class VaultInfoResponse
    {
        /// <summary>
        /// The vault ledger object.
        /// </summary>
        [JsonPropertyName("vault")]
        public LOVault Vault { get; set; }

        /// <summary>
        /// The identifying hash of the ledger version used to generate this response.
        /// </summary>
        [JsonPropertyName("ledger_hash")]
        public string LedgerHash { get; set; }

        /// <summary>
        /// The ledger index of the ledger version used to generate this response.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public uint? LedgerIndex { get; set; }

        /// <summary>
        /// If true, the information comes from a validated ledger version.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }
    }
}
