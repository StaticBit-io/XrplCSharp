using System.Text.Json.Serialization;

using System;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Utils;

// https://xrpl.org/docs/references/protocol/ledger-data/ledger-entry-types/credential

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// Flags for the Credential ledger entry.
    /// </summary>
    [Flags]
    public enum CredentialFlags : uint
    {
        /// <summary>
        /// If set, the credential has been accepted by the subject.
        /// A credential that has not been accepted is provisional and
        /// cannot be used for permissioned domain access.
        /// </summary>
        lsfAccepted = 0x00010000,
    }

    /// <summary>
    /// A Credential ledger entry represents a verifiable credential issued
    /// by one account (the issuer) to another (the subject). Credentials
    /// are used for on-chain identity verification and permissioned domain
    /// access control on the XRP Ledger.
    /// </summary>
    public class LOCredential : BaseLedgerEntry
    {
        public LOCredential()
        {
            LedgerEntryType = LedgerEntryType.Credential;
        }

        /// <summary>
        /// The account that is the subject (holder) of the credential.
        /// </summary>
        [JsonPropertyName("Subject")]
        public string Subject { get; set; }

        /// <summary>
        /// The account that issued the credential.
        /// </summary>
        [JsonPropertyName("Issuer")]
        public string Issuer { get; set; }

        private string _credentialType;

        /// <summary>
        /// A value identifying the type of credential, stored as a hex-encoded string.
        /// Automatically normalizes text input to hex on assignment.
        /// </summary>
        [JsonPropertyName("CredentialType")]
        public string CredentialType
        {
            get => _credentialType;
            set => _credentialType = HexStringHelper.NormalizeToHex(value, 64, nameof(CredentialType));
        }

        /// <summary>
        /// Decoded human-readable value of CredentialType (UTF-8, trimmed by 0x00).
        /// </summary>
        [JsonIgnore]
        public string CredentialTypeValue =>
            string.IsNullOrEmpty(_credentialType) ? null : HexStringHelper.FromHex(_credentialType);

        /// <summary>
        /// The time after which the credential expires, in seconds since the Ripple Epoch.
        /// </summary>
        [JsonPropertyName("Expiration")]
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Expiration { get; set; }

        private string _uri;

        /// <summary>
        /// An arbitrary URI reference for additional credential data, stored as a hex-encoded string.
        /// Automatically normalizes text input to hex on assignment.
        /// </summary>
        [JsonPropertyName("URI")]
        public string URI
        {
            get => _uri;
            set => _uri = HexStringHelper.NormalizeToHex(value, 256, nameof(URI));
        }

        /// <summary>
        /// Decoded human-readable value of URI (UTF-8, trimmed by 0x00).
        /// </summary>
        [JsonIgnore]
        public string URIValue =>
            string.IsNullOrEmpty(_uri) ? null : HexStringHelper.FromHex(_uri);

        /// <summary>
        /// A bit-map of boolean flags. See <see cref="CredentialFlags"/>.
        /// </summary>
        [JsonPropertyName("Flags")]
        public new uint Flags { get; set; }

        /// <summary>
        /// A hint indicating which page of the owner directory links to this entry.
        /// </summary>
        [JsonPropertyName("OwnerNode")]
        public string OwnerNode { get; set; }

        /// <summary>
        /// A hint indicating which page of the subject's owner directory links to this entry.
        /// </summary>
        [JsonPropertyName("SubjectNode")]
        public string SubjectNode { get; set; }

        /// <summary>
        /// A hint indicating which page of the issuer's owner directory links to this entry.
        /// </summary>
        [JsonPropertyName("IssuerNode")]
        public string IssuerNode { get; set; }

        /// <summary>
        /// The identifying hash of the transaction that most recently modified this entry.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// The index of the ledger that contains the transaction that most recently modified this entry.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint PreviousTxnLgrSeq { get; set; }
    }
}
