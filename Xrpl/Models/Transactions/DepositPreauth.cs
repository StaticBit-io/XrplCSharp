

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/transactions/depositPreauth.ts

using System.Collections.Generic;
using System.Threading.Tasks;

using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;

namespace Xrpl.Models.Transactions
{
    /// <inheritdoc cref="IDepositPreauth" />
    public class DepositPreauth : TransactionRequest, IDepositPreauth
    {
        public DepositPreauth()
        {
            TransactionType = TransactionType.DepositPreauth;
        }

        /// <inheritdoc />
        public string Authorize { get; set; }

        /// <inheritdoc />
        public string Unauthorize { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuthorizeCredentials")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AuthorizeCredentialEntry> AuthorizeCredentials { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("UnauthorizeCredentials")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AuthorizeCredentialEntry> UnauthorizeCredentials { get; set; }
    }

    /// <summary>
    /// A DepositPreauth transaction gives another account pre-approval to deliver  payments to the sender of this transaction.<br/>
    /// This is only useful if the sender  of this transaction is using (or plans to use) Deposit Authorization.<br/>
    /// XLS-70 extends this with credential-based preauthorization via <see cref="AuthorizeCredentials"/> /
    /// <see cref="UnauthorizeCredentials"/>.
    /// </summary>
    public interface IDepositPreauth : ITransactionCommon
    {
        /// <summary>
        /// The XRP Ledger address of the sender to preauthorize.
        /// </summary>
        string Authorize { get; set; }

        /// <summary>
        /// The XRP Ledger address of a sender whose preauthorization should be revoked.
        /// </summary>
        string Unauthorize { get; set; }

        /// <summary>
        /// (Optional, XLS-70) A set of 1..8 credentials whose holders are preauthorized to deliver payments
        /// to the account that submits this transaction.
        /// Each entry wraps an <see cref="AuthorizeCredentialBody"/> with Issuer + CredentialType.
        /// Mutually exclusive with <see cref="Authorize"/>, <see cref="Unauthorize"/> and <see cref="UnauthorizeCredentials"/>.
        /// </summary>
        List<AuthorizeCredentialEntry> AuthorizeCredentials { get; set; }

        /// <summary>
        /// (Optional, XLS-70) A set of 1..8 credentials to revoke from credential-based preauthorization.
        /// Each entry wraps an <see cref="AuthorizeCredentialBody"/> with Issuer + CredentialType.
        /// Mutually exclusive with <see cref="Authorize"/>, <see cref="Unauthorize"/> and <see cref="AuthorizeCredentials"/>.
        /// </summary>
        List<AuthorizeCredentialEntry> UnauthorizeCredentials { get; set; }
    }

    /// <inheritdoc cref="IDepositPreauth" />
    public class DepositPreauthResponse : TransactionResponse, IDepositPreauth
    {
        /// <inheritdoc />
        public string Authorize { get; set; }

        /// <inheritdoc />
        public string Unauthorize { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("AuthorizeCredentials")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AuthorizeCredentialEntry> AuthorizeCredentials { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("UnauthorizeCredentials")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AuthorizeCredentialEntry> UnauthorizeCredentials { get; set; }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of a DepositPreauth at runtime.
        /// </summary>
        /// <param name="tx"> A DepositPreauth Transaction.</param>
        /// <exception cref="ValidationException">When the DepositPreauth is malformed.</exception>
        public static async Task ValidateDepositPreauth(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            tx.TryGetValue("Authorize", out var Authorize);
            tx.TryGetValue("Unauthorize", out var Unauthorize);
            tx.TryGetValue("AuthorizeCredentials", out var AuthorizeCredentials);
            tx.TryGetValue("UnauthorizeCredentials", out var UnauthorizeCredentials);

            int populated = 0;
            if (Authorize is not null) populated++;
            if (Unauthorize is not null) populated++;
            if (AuthorizeCredentials is not null) populated++;
            if (UnauthorizeCredentials is not null) populated++;

            if (populated == 0)
                throw new ValidationException("DepositPreauth: must provide one of Authorize, Unauthorize, AuthorizeCredentials or UnauthorizeCredentials");

            if (populated > 1)
                throw new ValidationException("DepositPreauth: exactly one of Authorize, Unauthorize, AuthorizeCredentials or UnauthorizeCredentials must be provided");

            if (Authorize is not null)
            {
                if (Authorize is not string authorizeStr)
                    throw new ValidationException("DepositPreauth: Authorize must be a string");
                if (tx.TryGetValue("Account", out var account) && account is string accountStr && accountStr == authorizeStr)
                    throw new ValidationException("DepositPreauth: Account can't preauthorize its own address");
            }

            if (Unauthorize is not null)
            {
                if (Unauthorize is not string unauthorizeStr)
                    throw new ValidationException("DepositPreauth: Unauthorize must be a string");
                if (tx.TryGetValue("Account", out var account) && account is string accountStr && accountStr == unauthorizeStr)
                    throw new ValidationException("DepositPreauth: Account can't unauthorize its own address");
            }

            if (AuthorizeCredentials is not null)
            {
                CredentialsValidator.ValidateCredentialsList(AuthorizeCredentials, "DepositPreauth", "AuthorizeCredentials", isStringID: false);
            }

            if (UnauthorizeCredentials is not null)
            {
                CredentialsValidator.ValidateCredentialsList(UnauthorizeCredentials, "DepositPreauth", "UnauthorizeCredentials", isStringID: false);
            }
        }
    }

}
