// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/common.ts (validateCredentialsList)

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Shared validator for XLS-70 credential collections used across transactions.
    /// Mirrors the behavior of <c>validateCredentialsList</c> from xrpl.js.
    /// </summary>
    public static class CredentialsValidator
    {
        /// <summary>
        /// Maximum number of credentials allowed in a single XLS-70 list.
        /// </summary>
        public const int MaxCredentialsListLength = 8;

        /// <summary>
        /// Maximum length of <c>CredentialType</c> in hex characters (64 bytes -> 128 hex chars).
        /// </summary>
        public const int MaxCredentialTypeLengthHex = 128;

        private static readonly Regex HexObjectIdRegex = new Regex("^[0-9A-Fa-f]{64}$", RegexOptions.Compiled);
        private static readonly Regex HexCredentialTypeRegex = new Regex("^[0-9A-Fa-f]+$", RegexOptions.Compiled);

        /// <summary>
        /// Validates a list of credentials.
        /// Two list shapes are supported:
        ///  - When <paramref name="isStringID"/> is <c>true</c>, the list must contain hex object-IDs (64 hex characters each).
        ///    This is the format used by the <c>CredentialIDs</c> field on Payment, EscrowFinish, AccountDelete and PaymentChannelClaim.
        ///  - When <paramref name="isStringID"/> is <c>false</c>, the list must contain wrapped credential objects of the form
        ///    <c>{ Credential: { Issuer, CredentialType } }</c>. This is the format used by the <c>AuthorizeCredentials</c> /
        ///    <c>UnauthorizeCredentials</c> fields on the DepositPreauth transaction.
        /// </summary>
        /// <param name="credentials">The raw list value extracted from the transaction dictionary.</param>
        /// <param name="txType">The transaction type name used for error messages (e.g. "Payment").</param>
        /// <param name="fieldName">The field name used for error messages (e.g. "CredentialIDs").</param>
        /// <param name="isStringID">Determines the expected element shape (see above).</param>
        /// <param name="maxLength">Maximum allowed list length. Defaults to <see cref="MaxCredentialsListLength"/>.</param>
        /// <exception cref="ValidationException">Thrown when the list is malformed.</exception>
        public static void ValidateCredentialsList(
            object credentials,
            string txType,
            string fieldName,
            bool isStringID,
            int maxLength = MaxCredentialsListLength)
        {
            if (credentials is null)
            {
                return;
            }

            List<object> list = ToList(credentials);
            if (list is null)
            {
                throw new ValidationException($"{txType}: {fieldName} must be an array");
            }

            if (list.Count == 0)
            {
                throw new ValidationException($"{txType}: {fieldName} cannot be empty");
            }

            if (list.Count > maxLength)
            {
                throw new ValidationException($"{txType}: {fieldName} cannot contain more than {maxLength} elements");
            }

            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                object element = list[i];
                if (isStringID)
                {
                    string id = element as string;
                    if (string.IsNullOrEmpty(id))
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}] must be a non-empty string");
                    }

                    if (!HexObjectIdRegex.IsMatch(id))
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}] must be a 64-character hexadecimal object ID");
                    }

                    string key = id.ToUpperInvariant();
                    if (!seen.Add(key))
                    {
                        throw new ValidationException($"{txType}: {fieldName} cannot contain duplicate credential IDs");
                    }
                }
                else
                {
                    Dictionary<string, object> wrapper = ToDictionary(element);
                    if (wrapper is null || !wrapper.TryGetValue("Credential", out object credentialObj))
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}] must be an object with a Credential field");
                    }

                    Dictionary<string, object> credential = ToDictionary(credentialObj);
                    if (credential is null)
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}].Credential must be an object");
                    }

                    credential.TryGetValue("Issuer", out object issuerObj);
                    credential.TryGetValue("CredentialType", out object credentialTypeObj);

                    string issuer = issuerObj as string;
                    if (string.IsNullOrEmpty(issuer))
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}].Credential.Issuer is required and must be a string");
                    }

                    string credentialType = credentialTypeObj as string;
                    if (string.IsNullOrEmpty(credentialType))
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}].Credential.CredentialType is required and must be a string");
                    }

                    if (credentialType.Length > MaxCredentialTypeLengthHex)
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}].Credential.CredentialType cannot exceed 64 bytes (128 hex characters)");
                    }

                    if (!HexCredentialTypeRegex.IsMatch(credentialType))
                    {
                        throw new ValidationException($"{txType}: {fieldName}[{i}].Credential.CredentialType must be a hexadecimal string");
                    }

                    string key = $"{issuer}:{credentialType.ToUpperInvariant()}";
                    if (!seen.Add(key))
                    {
                        throw new ValidationException($"{txType}: {fieldName} cannot contain duplicate credentials");
                    }
                }
            }
        }

        private static List<object> ToList(object value)
        {
            if (value is List<object> direct)
            {
                return direct;
            }

            if (value is IEnumerable<object> enumerable)
            {
                return enumerable.ToList();
            }

            if (value is System.Collections.IEnumerable nonGeneric and not string)
            {
                List<object> result = new List<object>();
                foreach (object item in nonGeneric)
                {
                    result.Add(item);
                }

                return result;
            }

            return null;
        }

        private static Dictionary<string, object> ToDictionary(object value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is Dictionary<string, object> direct)
            {
                return direct;
            }

            if (value is Dictionary<string, dynamic> dyn)
            {
                Dictionary<string, object> result = new Dictionary<string, object>(dyn.Count);
                foreach (KeyValuePair<string, dynamic> kv in dyn)
                {
                    result[kv.Key] = kv.Value;
                }

                return result;
            }

            if (value is IDictionary<string, object> idict)
            {
                return idict.ToDictionary(k => k.Key, k => k.Value);
            }

            if (value is System.Collections.IDictionary nonGeneric)
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                foreach (System.Collections.DictionaryEntry entry in nonGeneric)
                {
                    if (entry.Key is string key)
                    {
                        result[key] = entry.Value;
                    }
                }

                return result;
            }

            return null;
        }
    }
}
