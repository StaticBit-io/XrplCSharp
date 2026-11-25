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

        /// <summary>
        /// The account that created the vault. Together with <see cref="Seq"/> it identifies the
        /// vault in place of <see cref="VaultID"/>; the node rejects a request that mixes the two.
        /// </summary>
        [JsonPropertyName("owner")]
        public string Owner { get; set; }

        /// <summary>
        /// The sequence number of the VaultCreate transaction that created the vault.
        /// </summary>
        [JsonPropertyName("seq")]
        public uint? Seq { get; set; }
    }

    /// <summary>
    /// Response expected from a <see cref="VaultInfoRequest"/>.
    /// </summary>
    public class VaultInfoResponse : BaseMethodResult
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

        /// <summary>
        /// The ledger index of the open ledger the data was read from. Present instead of
        /// <c>ledger_index</c> and <c>ledger_hash</c> when the request did not name a closed ledger.
        /// </summary>
        [JsonPropertyName("ledger_current_index")]
        public uint? LedgerCurrentIndex { get; set; }
    }
}
