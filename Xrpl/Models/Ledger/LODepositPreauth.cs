// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/ledger/DepositPreauth.ts
// https://xrpl.org/docs/references/protocol/ledger-data/ledger-entry-types/depositpreauth

using System.Text.Json.Serialization;

using System.Collections.Generic;
using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// A DepositPreauth object tracks a preauthorization from one account to another.<br/>
    /// DepositPreauth transactions create these objects.<br/>
    /// Preauthorization can be granted either to a specific account (<see cref="Authorize"/>)
    /// or to any account that holds a matching set of credentials (<see cref="AuthorizeCredentials"/>, XLS-70).
    /// Exactly one of these two fields is populated per ledger entry.
    /// </summary>
    public class LODepositPreauth : BaseLedgerEntry
    {
        public LODepositPreauth()
        {
            LedgerEntryType = LedgerEntryType.DepositPreauth;
        }

        /// <summary>
        /// The account that granted the preauthorization (the destination of the future payments).
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// The account that received the address-based preauthorization.<br/>
        /// Null when the entry uses credential-based preauthorization (<see cref="AuthorizeCredentials"/> is set instead).
        /// </summary>
        public string Authorize { get; set; }

        /// <summary>
        /// The set of credentials that received the preauthorization.<br/>
        /// Each entry wraps a <see cref="AuthorizeCredentialBody"/> with Issuer + CredentialType.<br/>
        /// Null when the entry uses address-based preauthorization (<see cref="Authorize"/> is set instead).
        /// </summary>
        [JsonPropertyName("AuthorizeCredentials")]
        public List<AuthorizeCredentialEntry> AuthorizeCredentials { get; set; }

        /// <summary>
        /// A bit-map of boolean flags. No flags are currently defined for DepositPreauth, so this value is always 0.
        /// </summary>
        [JsonConverter(typeof(NumberOrStringConverter))]
        public string Flags { get; set; }

        /// <summary>
        /// A hint indicating which page of the sender's owner directory links to this object,
        /// in case the directory consists of multiple pages.
        /// </summary>
        public string OwnerNode { get; set; }

        /// <summary>
        /// The identifying hash of the transaction that most recently modified this object.
        /// </summary>
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// The index of the ledger that contains the transaction that most recently modified this object.
        /// </summary>
        public uint PreviousTxnLgrSeq { get; set; }
    }

    /// <summary>
    /// Wrapper element used inside <see cref="LODepositPreauth.AuthorizeCredentials"/>.<br/>
    /// rippled serializes each credential reference as a single-property object: <c>{ "Credential": { ... } }</c>.
    /// </summary>
    public class AuthorizeCredentialEntry
    {
        [JsonPropertyName("Credential")]
        public AuthorizeCredentialBody Credential { get; set; }
    }

    /// <summary>
    /// Body of a single credential reference inside a credential-based DepositPreauth entry.
    /// </summary>
    public class AuthorizeCredentialBody
    {
        /// <summary>
        /// The account that issued the credential.
        /// </summary>
        [JsonPropertyName("Issuer")]
        public string Issuer { get; set; }

        /// <summary>
        /// A hex-encoded value identifying the type of credential.
        /// </summary>
        [JsonPropertyName("CredentialType")]
        public string CredentialType { get; set; }
    }
}
