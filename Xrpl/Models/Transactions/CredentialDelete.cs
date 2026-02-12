using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Utils;

// https://xrpl.org/docs/references/protocol/transactions/types/credentialdelete

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Deletes a credential from the XRP Ledger. Can be submitted by:
    /// - The issuer of the credential (to revoke it).
    /// - The subject of the credential (to un-accept it).
    /// - Any account, if the credential has expired.
    /// Requires the Credentials amendment.
    /// </summary>
    public interface ICredentialDelete : ITransactionCommon
    {
        /// <summary>
        /// (Optional) The subject of the credential. If omitted, the Account
        /// (transaction sender) is assumed to be the subject.
        /// </summary>
        string Subject { get; set; }

        /// <summary>
        /// (Optional) The issuer of the credential. If omitted, the Account
        /// (transaction sender) is assumed to be the issuer.
        /// </summary>
        string Issuer { get; set; }

        /// <summary>
        /// The type of credential to delete, as a hex-encoded string.
        /// </summary>
        string CredentialType { get; set; }
    }

    /// <inheritdoc cref="ICredentialDelete" />
    public class CredentialDelete : TransactionRequest, ICredentialDelete
    {
        public CredentialDelete()
        {
            TransactionType = TransactionType.CredentialDelete;
        }

        /// <inheritdoc />
        [JsonProperty("Subject")]
        public string Subject { get; set; }

        /// <inheritdoc />
        [JsonProperty("Issuer")]
        public string Issuer { get; set; }

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
    }

    /// <summary>
    /// Response for a CredentialDelete transaction.
    /// </summary>
    public class CredentialDeleteResponse : TransactionResponse, ICredentialDelete
    {
        /// <inheritdoc />
        [JsonProperty("Subject")]
        public string Subject { get; set; }

        /// <inheritdoc />
        [JsonProperty("Issuer")]
        public string Issuer { get; set; }

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
    }

    /// <summary>
    /// Validation for CredentialDelete transactions.
    /// </summary>
    public partial class Validation
    {
        /// <summary>
        /// Validates a CredentialDelete transaction.
        /// </summary>
        /// <param name="tx">A CredentialDelete transaction.</param>
        /// <exception cref="ValidationException">When the CredentialDelete is malformed.</exception>
        public static async Task ValidateCredentialDelete(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            tx.TryGetValue("Subject", out var subject);
            tx.TryGetValue("Issuer", out var issuer);

            bool hasSubject = subject is string subjectStr && !string.IsNullOrEmpty(subjectStr);
            bool hasIssuer = issuer is string issuerStr && !string.IsNullOrEmpty(issuerStr);

            if (!hasSubject && !hasIssuer)
            {
                throw new ValidationException("CredentialDelete: at least one of Subject or Issuer must be provided");
            }

            tx.TryGetValue("CredentialType", out var credentialType);
            if (credentialType is not string credTypeStr || string.IsNullOrEmpty(credTypeStr))
            {
                throw new ValidationException("CredentialDelete: CredentialType is required");
            }

            if (credTypeStr.Length > MaxCredentialTypeLength)
            {
                throw new ValidationException("CredentialDelete: CredentialType cannot exceed 64 bytes (128 hex characters)");
            }
        }
    }
}
