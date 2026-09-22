using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
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
        /// The account to delegate permissions to (sfAuthorize in PermissionDelegationV1_1).
        /// </summary>
        string Authorize { get; set; }

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
        [JsonPropertyName("Authorize")]
        public string Authorize { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Permissions")]
        public List<PermissionWrapper> Permissions { get; set; }
    }

    /// <inheritdoc cref="IDelegateSet" />
    public class DelegateSetResponse : TransactionResponse, IDelegateSet
    {
        /// <inheritdoc />
        [JsonPropertyName("Authorize")]
        public string Authorize { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Permissions")]
        public List<PermissionWrapper> Permissions { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Transaction types rippled refuses to delegate, per <c>transactions.macro</c>:
        /// <c>TxSettings.delegable</c> defaults to <c>Delegation::NotDelegable</c>, and a type is
        /// delegable only where the macro says so explicitly.
        /// </summary>
        /// <remarks>
        /// Checking this locally is sound because the flag is static. An amendment can withhold
        /// delegability from a delegable type - <c>Permission::isDelegable</c> takes the network's
        /// Rules - but it can never grant it to a forbidden one, so a type listed here is refused
        /// on every network. The converse set, the delegable types, could not be checked this way.
        /// <para>
        /// Held against the vendored macro by <c>TestUDelegateSet.TestDenyListMatchesRippledMacro</c>,
        /// which catches a typo or a type the protocol added later. The names go through
        /// <c>nameof</c> so the compiler catches the case that test cannot: a type renamed or
        /// removed from <see cref="TransactionType"/> leaves a string here that matches nothing.
        /// </para>
        /// </remarks>
        internal static readonly HashSet<string> NonDelegableTransactions = new(StringComparer.Ordinal)
        {
            nameof(TransactionType.AccountDelete),
            nameof(TransactionType.AccountSet),
            nameof(TransactionType.Batch),
            nameof(TransactionType.ConfidentialMPTConvert),
            nameof(TransactionType.DelegateSet),
            nameof(TransactionType.EnableAmendment),
            nameof(TransactionType.LoanBrokerCoverClawback),
            nameof(TransactionType.LoanBrokerCoverDeposit),
            nameof(TransactionType.LoanBrokerCoverWithdraw),
            nameof(TransactionType.LoanBrokerDelete),
            nameof(TransactionType.LoanBrokerSet),
            nameof(TransactionType.LoanDelete),
            nameof(TransactionType.LoanManage),
            nameof(TransactionType.LoanPay),
            nameof(TransactionType.LoanSet),
            nameof(TransactionType.SetFee),
            nameof(TransactionType.SetRegularKey),
            nameof(TransactionType.SignerListSet),
            nameof(TransactionType.SponsorshipTransfer),
            nameof(TransactionType.UNLModify),
            nameof(TransactionType.VaultClawback),
            nameof(TransactionType.VaultCreate),
            nameof(TransactionType.VaultDelete),
            nameof(TransactionType.VaultDeposit),
            nameof(TransactionType.VaultSet),
            nameof(TransactionType.VaultWithdraw),
        };

        private const int MaxPermissions = 10;

        /// <summary>
        /// Resolves a permission to the name rippled uses for it, and rejects it if that names a
        /// transaction type which cannot be delegated.
        /// </summary>
        /// <remarks>
        /// The value arrives as a name from a node and as a number from the typed model, so both
        /// the deny list and duplicate detection have to work on one spelling: rippled compares the
        /// numeric <c>sfPermissionValue</c>, which makes <c>Payment</c> and <c>1</c> the same
        /// entry. The name is the canonical form here rather than the number, because two values
        /// this version cannot name must stay distinct - collapsing them onto one sentinel would
        /// invent a duplicate the node does not see.
        /// <para>
        /// A granular permission resolves to its own name and is never in the list, which is
        /// correct: rippled allows one even when the underlying type is not delegable.
        /// </para>
        /// </remarks>
        /// <returns>The canonical spelling to compare entries by.</returns>
        private static string CanonicalPermission(string permissionValue)
        {
            string name = permissionValue;

            if (uint.TryParse(permissionValue, NumberStyles.None, CultureInfo.InvariantCulture, out uint numeric))
            {
                // Left as written when it cannot be named, so two unnameable values stay distinct.
                if (!PermissionValueConverter.TryGetPermissionName(numeric, out string resolved))
                    return permissionValue;

                name = resolved;
            }

            if (NonDelegableTransactions.Contains(name))
                throw new ValidationException($"DelegateSet: transaction type '{name}' cannot be delegated");

            return name;
        }

        public static void ValidateDelegateSet(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Authorize", out var auth) || auth is not string authorize)
                throw new ValidationException("DelegateSet: missing field Authorize");

            // rippled's preflight rejects an account delegating to itself with temMALFORMED.
            if (tx.TryGetValue("Account", out object account)
                && string.Equals(account as string, authorize, StringComparison.Ordinal))
            {
                throw new ValidationException("DelegateSet: Authorize and Account must be different");
            }

            if (!tx.TryGetValue("Permissions", out var perms) || perms is null)
                throw new ValidationException("DelegateSet: missing field Permissions");

            // Validate Permissions is an array
            if (perms is JsonElement je)
            {
                if (je.ValueKind != JsonValueKind.Array)
                    throw new ValidationException("DelegateSet: Permissions must be an array");

                // An empty array is how a delegation is revoked: rippled's doApply deletes the
                // Delegate ledger object when Permissions carries no entries.
                int count = je.GetArrayLength();
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

                    string canonical = CanonicalPermission(permValueStr);

                    if (!seen.Add(canonical))
                        throw new ValidationException($"DelegateSet: duplicate PermissionValue '{canonical}'");
                }
            }
            else if (perms is IList<object> list)
            {
                if (list.Count > MaxPermissions)
                    throw new ValidationException($"DelegateSet: Permissions must have at most {MaxPermissions} entries");

                HashSet<string> seen = new();
                foreach (object entry in list)
                {
                    // Each entry should be a dict-like object with a "Permission" sub-object
                    if (entry is not IDictionary<string, object> entryDict)
                        throw new ValidationException("DelegateSet: each Permission entry must be an object");

                    if (!entryDict.TryGetValue("Permission", out object permObj) || permObj is not IDictionary<string, object> perm)
                        throw new ValidationException("DelegateSet: each entry must contain a Permission object");

                    if (!perm.TryGetValue("PermissionValue", out object permVal))
                        throw new ValidationException("DelegateSet: each Permission must contain PermissionValue");

                    string permValueStr = permVal?.ToString();
                    if (string.IsNullOrWhiteSpace(permValueStr))
                        throw new ValidationException("DelegateSet: PermissionValue must not be empty");

                    string canonical = CanonicalPermission(permValueStr);

                    if (!seen.Add(canonical))
                        throw new ValidationException($"DelegateSet: duplicate PermissionValue '{canonical}'");
                }
            }
            else
            {
                throw new ValidationException("DelegateSet: Permissions must be an array");
            }
        }
    }
}
