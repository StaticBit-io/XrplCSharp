using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;

// https://xrpl.org/docs/references/protocol/transactions/types/permissioneddomainset

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Create a permissioned domain, or modify one that you own.
    /// Requires the PermissionedDomains amendment.
    /// </summary>
    public interface IPermissionedDomainSet : ITransactionCommon
    {
        /// <summary>
        /// The ledger entry ID of an existing permissioned domain to modify.
        /// If omitted, creates a new permissioned domain.
        /// </summary>
        string DomainID { get; set; }

        /// <summary>
        /// A list of 1 to 10 AcceptedCredential objects that grant access to this domain.
        /// The list does not need to be sorted, but it cannot contain duplicates.
        /// When modifying an existing domain, this list replaces the existing list.
        /// </summary>
        List<AcceptedCredentialWrapper> AcceptedCredentials { get; set; }
    }

    /// <inheritdoc cref="IPermissionedDomainSet" />
    public class PermissionedDomainSet : TransactionRequest, IPermissionedDomainSet
    {
        /// <summary>
        /// Initializes a new instance of the PermissionedDomainSet class.
        /// </summary>
        public PermissionedDomainSet()
        {
            TransactionType = TransactionType.PermissionedDomainSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AcceptedCredentials")]
        public List<AcceptedCredentialWrapper> AcceptedCredentials { get; set; }
    }

    /// <summary>
    /// Response for a PermissionedDomainSet transaction.
    /// </summary>
    public class PermissionedDomainSetResponse : TransactionResponse, IPermissionedDomainSet
    {
        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AcceptedCredentials")]
        public List<AcceptedCredentialWrapper> AcceptedCredentials { get; set; }
    }

    /// <summary>
    /// Wrapper for a Credential object in the AcceptedCredentials array.
    /// Each member of the AcceptedCredentials array is an inner object named Credential.
    /// </summary>
    public class AcceptedCredentialWrapper
    {
        /// <summary>
        /// The credential object containing issuer and credential type.
        /// </summary>
        [JsonPropertyName("Credential")]
        public AcceptedCredential Credential { get; set; }
    }

    /// <summary>
    /// Represents a credential that grants access to a permissioned domain.
    /// </summary>
    public class AcceptedCredential
    {
        /// <summary>
        /// The issuer of the credential.
        /// </summary>
        [JsonPropertyName("Issuer")]
        public string Issuer { get; set; }

        /// <summary>
        /// The type of credential, as hexadecimal.
        /// This is an arbitrary value from 1 to 64 bytes that the issuer sets when they issue a credential.
        /// </summary>
        [JsonPropertyName("CredentialType")]
        public string CredentialType { get; set; }
    }

    /// <summary>
    /// Validation for PermissionedDomainSet transactions.
    /// </summary>
    public partial class Validation
    {
        private const int MaxCredentialTypeLength = 128;

        /// <summary>
        /// Validates a PermissionedDomainSet transaction.
        /// </summary>
        /// <param name="tx">A PermissionedDomainSet transaction.</param>
        /// <exception cref="ValidationException">When the PermissionedDomainSet is malformed.</exception>
        public static async Task ValidatePermissionedDomainSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            tx.TryGetValue("AcceptedCredentials", out var acceptedCredentials);

            if (acceptedCredentials == null)
            {
                throw new ValidationException("PermissionedDomainSet: AcceptedCredentials is required");
            }

            var credentialsList = acceptedCredentials as IList<object> ?? (acceptedCredentials as IEnumerable<object>)?.ToList();
            if (credentialsList == null || credentialsList.Count == 0)
            {
                throw new ValidationException("PermissionedDomainSet: AcceptedCredentials must contain at least 1 credential");
            }

            if (credentialsList.Count > 10)
            {
                throw new ValidationException("PermissionedDomainSet: AcceptedCredentials cannot contain more than 10 credentials");
            }

            var seen = new HashSet<string>();
            foreach (var item in credentialsList)
            {
                Dictionary<string, object> wrapper = null;
                if (item is Dictionary<string, object> dict)
                {
                    wrapper = dict;
                }
                else if (item is IDictionary<string, object> idict)
                {
                    wrapper = idict.ToDictionary(k => k.Key, k => (object)k.Value);
                }

                if (wrapper == null || !wrapper.TryGetValue("Credential", out var credentialObj))
                {
                    throw new ValidationException("PermissionedDomainSet: Each AcceptedCredentials entry must have a Credential object");
                }

                Dictionary<string, object> credential = null;
                if (credentialObj is Dictionary<string, object> credDict)
                {
                    credential = credDict;
                }
                else if (credentialObj is IDictionary<string, object> credIdict)
                {
                    credential = credIdict.ToDictionary(k => k.Key, k => (object)k.Value);
                }

                if (credential == null)
                {
                    throw new ValidationException("PermissionedDomainSet: Each AcceptedCredentials entry must have a Credential object");
                }

                credential.TryGetValue("Issuer", out var issuer);
                credential.TryGetValue("CredentialType", out var credentialType);

                var issuerStr = issuer as string;
                if (string.IsNullOrEmpty(issuerStr))
                {
                    throw new ValidationException("PermissionedDomainSet: Credential.Issuer is required");
                }

                var credTypeStr = credentialType as string;
                if (string.IsNullOrEmpty(credTypeStr))
                {
                    throw new ValidationException("PermissionedDomainSet: Credential.CredentialType is required");
                }

                if (credTypeStr.Length > MaxCredentialTypeLength)
                {
                    throw new ValidationException("PermissionedDomainSet: Credential.CredentialType cannot exceed 64 bytes (128 hex characters)");
                }

                var key = $"{issuerStr}:{credTypeStr.ToUpperInvariant()}";
                if (!seen.Add(key))
                {
                    throw new ValidationException("PermissionedDomainSet: AcceptedCredentials cannot contain duplicate credentials");
                }
            }
        }
    }
}
