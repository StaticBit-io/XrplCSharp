using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The DelegateSet transaction grants permissions to another account to send
    /// transactions on your behalf.
    /// </summary>
    public interface IDelegateSet : ITransactionCommon
    {
        /// <summary>
        /// The account to delegate permissions to.
        /// </summary>
        string Delegate { get; set; }

        /// <summary>
        /// An array of permission objects defining which transaction types the delegate can submit.
        /// </summary>
        List<PermissionWrapper> Permissions { get; set; }
    }

    /// <inheritdoc cref="IDelegateSet" />
    public class DelegateSet : TransactionRequest, IDelegateSet
    {
        public DelegateSet()
        {
            TransactionType = TransactionType.DelegateSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("Delegate")]
        public string Delegate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Permissions")]
        public List<PermissionWrapper> Permissions { get; set; }
    }

    /// <inheritdoc cref="IDelegateSet" />
    public class DelegateSetResponse : TransactionResponse, IDelegateSet
    {
        /// <inheritdoc />
        [JsonPropertyName("Delegate")]
        public string Delegate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Permissions")]
        public List<PermissionWrapper> Permissions { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Transaction types that cannot be delegated (per xrpl.js).
        /// </summary>
        private static readonly HashSet<string> NonDelegableTransactions = new(StringComparer.OrdinalIgnoreCase)
        {
            "AccountSet", "SetRegularKey", "SignerListSet", "DelegateSet"
        };

        private const int MaxPermissions = 10;

        public static async Task ValidateDelegateSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Delegate", out var auth) || auth is not string)
                throw new ValidationException("DelegateSet: missing field Delegate");

            if (!tx.TryGetValue("Permissions", out var perms) || perms is null)
                throw new ValidationException("DelegateSet: missing field Permissions");

            // Validate Permissions is an array
            if (perms is JsonElement je)
            {
                if (je.ValueKind != JsonValueKind.Array)
                    throw new ValidationException("DelegateSet: Permissions must be an array");

                int count = je.GetArrayLength();
                if (count == 0)
                    throw new ValidationException("DelegateSet: Permissions must not be empty");
                if (count > MaxPermissions)
                    throw new ValidationException($"DelegateSet: Permissions must have at most {MaxPermissions} entries");

                HashSet<string> seen = new();
                foreach (JsonElement item in je.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        throw new ValidationException("DelegateSet: each Permission entry must be an object");

                    if (!item.TryGetProperty("Permission", out JsonElement permObj) || permObj.ValueKind != JsonValueKind.Object)
                        throw new ValidationException("DelegateSet: each entry must contain a Permission object");

                    if (!permObj.TryGetProperty("PermissionValue", out JsonElement permVal))
                        throw new ValidationException("DelegateSet: each Permission must contain PermissionValue");

                    string permValueStr = permVal.ToString();
                    if (string.IsNullOrWhiteSpace(permValueStr))
                        throw new ValidationException("DelegateSet: PermissionValue must not be empty");

                    if (NonDelegableTransactions.Contains(permValueStr))
                        throw new ValidationException($"DelegateSet: transaction type '{permValueStr}' cannot be delegated");

                    if (!seen.Add(permValueStr))
                        throw new ValidationException($"DelegateSet: duplicate PermissionValue '{permValueStr}'");
                }
            }
            else if (perms is IList<object> list)
            {
                if (list.Count == 0)
                    throw new ValidationException("DelegateSet: Permissions must not be empty");
                if (list.Count > MaxPermissions)
                    throw new ValidationException($"DelegateSet: Permissions must have at most {MaxPermissions} entries");
            }
            else
            {
                throw new ValidationException("DelegateSet: Permissions must be an array");
            }
        }
    }
}
