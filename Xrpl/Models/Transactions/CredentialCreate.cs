using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Utils;

// https://xrpl.org/docs/references/protocol/transactions/types/credentialcreate

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Creates a credential on the XRP Ledger. The credential is issued by the
    /// transaction sender (Account) to a Subject. The credential is not valid
    /// until the Subject accepts it using CredentialAccept.
    /// Requires the Credentials amendment.
    /// </summary>
    public interface ICredentialCreate : ITransactionCommon
    {
        /// <summary>
        /// The account that is the subject (recipient) of the credential.
        /// </summary>
        string Subject { get; set; }

        /// <summary>
        /// A value to identify the type of credential, as a hex-encoded string.
        /// This must be between 1 and 64 bytes (2 to 128 hex characters).
        /// </summary>
        string CredentialType { get; set; }

        /// <summary>
        /// (Optional) The time after which the credential expires, in seconds
        /// since the Ripple Epoch.
        /// </summary>
        DateTime? Expiration { get; set; }

        /// <summary>
        /// (Optional) An arbitrary URI reference for additional credential data,
        /// as a hex-encoded string. Can be at most 256 bytes (512 hex characters).
        /// </summary>
        string URI { get; set; }
    }

    /// <inheritdoc cref="ICredentialCreate" />
    public class CredentialCreate : TransactionRequest, ICredentialCreate
    {
        public CredentialCreate()
        {
            TransactionType = TransactionType.CredentialCreate;
        }

        /// <inheritdoc />
        [JsonProperty("Subject")]
        public string Subject { get; set; }

        private string _credentialType;

        /// <inheritdoc />
        [JsonProperty("CredentialType")]
        public string CredentialType
        {
            get => _credentialType;
            set => _credentialType = HexStringHelper.NormalizeToHex(value, 64, nameof(CredentialType));
        }

        /// <summary>
        /// Decoded human-readable value of CredentialType (UTF-8).
        /// </summary>
        [JsonIgnore]
        public string CredentialTypeValue =>
            string.IsNullOrEmpty(_credentialType) ? null : HexStringHelper.FromHex(_credentialType);

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        [JsonProperty("Expiration")]
        public DateTime? Expiration { get; set; }

        private string _uri;

        /// <inheritdoc />
        [JsonProperty("URI")]
        public string URI
        {
            get => _uri;
            set => _uri = HexStringHelper.NormalizeToHex(value, 256, nameof(URI));
        }

        /// <summary>
        /// Decoded human-readable value of URI (UTF-8).
        /// </summary>
        [JsonIgnore]
        public string URIValue =>
            string.IsNullOrEmpty(_uri) ? null : HexStringHelper.FromHex(_uri);
    }

    /// <summary>
    /// Response for a CredentialCreate transaction.
    /// </summary>
    public class CredentialCreateResponse : TransactionResponse, ICredentialCreate
    {
        /// <inheritdoc />
        [JsonProperty("Subject")]
        public string Subject { get; set; }

        private string _credentialType;

        /// <inheritdoc />
        [JsonProperty("CredentialType")]
        public string CredentialType
        {
            get => _credentialType;
            set => _credentialType = HexStringHelper.NormalizeToHex(value, 64, nameof(CredentialType));
        }

        /// <summary>
        /// Decoded human-readable value of CredentialType (UTF-8).
        /// </summary>
        [JsonIgnore]
        public string CredentialTypeValue =>
            string.IsNullOrEmpty(_credentialType) ? null : HexStringHelper.FromHex(_credentialType);

        /// <inheritdoc />
        [JsonConverter(typeof(RippleDateTimeConverter))]
        [JsonProperty("Expiration")]
        public DateTime? Expiration { get; set; }

        private string _uri;

        /// <inheritdoc />
        [JsonProperty("URI")]
        public string URI
        {
            get => _uri;
            set => _uri = HexStringHelper.NormalizeToHex(value, 256, nameof(URI));
        }

        /// <summary>
        /// Decoded human-readable value of URI (UTF-8).
        /// </summary>
        [JsonIgnore]
        public string URIValue =>
            string.IsNullOrEmpty(_uri) ? null : HexStringHelper.FromHex(_uri);
    }

    /// <summary>
    /// Validation for CredentialCreate transactions.
    /// </summary>
    public partial class Validation
    {
        private const int MaxCredentialURILength = 512;

        /// <summary>
        /// Validates a CredentialCreate transaction.
        /// </summary>
        /// <param name="tx">A CredentialCreate transaction.</param>
        /// <exception cref="ValidationException">When the CredentialCreate is malformed.</exception>
        public static async Task ValidateCredentialCreate(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            tx.TryGetValue("Subject", out var subject);
            if (subject is not string subjectStr || string.IsNullOrEmpty(subjectStr))
            {
                throw new ValidationException("CredentialCreate: Subject is required");
            }

            tx.TryGetValue("CredentialType", out var credentialType);
            if (credentialType is not string credTypeStr || string.IsNullOrEmpty(credTypeStr))
            {
                throw new ValidationException("CredentialCreate: CredentialType is required");
            }

            if (credTypeStr.Length > MaxCredentialTypeLength)
            {
                throw new ValidationException("CredentialCreate: CredentialType cannot exceed 64 bytes (128 hex characters)");
            }

            tx.TryGetValue("URI", out var uri);
            if (uri is string uriStr && uriStr.Length > MaxCredentialURILength)
            {
                throw new ValidationException("CredentialCreate: URI cannot exceed 256 bytes (512 hex characters)");
            }
        }
    }
}
