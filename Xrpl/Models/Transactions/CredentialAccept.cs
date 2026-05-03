using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Utils;

// https://xrpl.org/docs/references/protocol/transactions/types/credentialaccept

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Accepts a credential that has been provisionally issued to the Account.
    /// Once accepted, the credential becomes valid on the ledger and the reserve
    /// responsibility transfers from the issuer to the subject (Account).
    /// Requires the Credentials amendment.
    /// </summary>
    public interface ICredentialAccept : ITransactionCommon
    {
        /// <summary>
        /// The issuer of the credential to accept.
        /// </summary>
        string Issuer { get; set; }

        /// <summary>
        /// The type of credential to accept, as a hex-encoded string.
        /// </summary>
        string CredentialType { get; set; }
    }

    /// <inheritdoc cref="ICredentialAccept" />
    public class CredentialAccept : TransactionRequest, ICredentialAccept
    {
        public CredentialAccept()
        {
            TransactionType = TransactionType.CredentialAccept;
        }

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
    /// Response for a CredentialAccept transaction.
    /// </summary>
    public class CredentialAcceptResponse : TransactionResponse, ICredentialAccept
    {
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
    /// Validation for CredentialAccept transactions.
    /// </summary>
    public partial class Validation
    {
        /// <summary>
        /// Validates a CredentialAccept transaction.
        /// </summary>
        /// <param name="tx">A CredentialAccept transaction.</param>
        /// <exception cref="ValidationException">When the CredentialAccept is malformed.</exception>
        public static async Task ValidateCredentialAccept(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            tx.TryGetValue("Issuer", out var issuer);
            if (issuer is not string issuerStr || string.IsNullOrEmpty(issuerStr))
            {
                throw new ValidationException("CredentialAccept: Issuer is required");
            }

            tx.TryGetValue("CredentialType", out var credentialType);
            if (credentialType is not string credTypeStr || string.IsNullOrEmpty(credTypeStr))
            {
                throw new ValidationException("CredentialAccept: CredentialType is required");
            }

            if (credTypeStr.Length > MaxCredentialTypeLength)
            {
                throw new ValidationException("CredentialAccept: CredentialType cannot exceed 64 bytes (128 hex characters)");
            }
        }
    }
}
