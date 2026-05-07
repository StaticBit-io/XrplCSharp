using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

using JsonSerializer = System.Text.Json.JsonSerializer;

using Currency = Xrpl.Models.Common.Currency;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Claw back tokens issued by your account. Issuers can only claw back trust line tokens
    /// if they enabled the Allow Trust Line Clawback setting before issuing any tokens.
    /// Issuers can claw back MPTs if the corresponding MPT Issuance has clawback enabled.
    /// </summary>
    public class ClawBack : TransactionRequest, IClawBack
    {
        public ClawBack()
        {
            TransactionType = TransactionType.Clawback;
        }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Xrpl.Models.Common.Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }
    }

    /// <summary>
    /// Claw back tokens issued by your account. Issuers can only claw back trust line tokens
    /// if they enabled the Allow Trust Line Clawback setting before issuing any tokens.
    /// Issuers can claw back MPTs if the corresponding MPT Issuance has clawback enabled.
    /// </summary>
    public interface IClawBack : ITransactionCommon
    {
        /// <summary>
        /// The amount to claw back. The quantity in the value sub-field must not be zero.
        /// If this is more than the current balance, the transaction claws back the entire balance.
        /// When clawing back trust line tokens, the issuer sub-field indicates the token holder
        /// to claw back tokens from.
        /// </summary>
        public Xrpl.Models.Common.Currency Amount { get; set; }

        /// <summary>
        /// The holder to claw back tokens from, if clawing back MPTs. The holder must have
        /// a non-zero balance of the MPT issuance indicated in the Amount field.
        /// Required for MPT clawback, must be omitted for trust line token clawback.
        /// </summary>
        public string Holder { get; set; }
    }

    /// <inheritdoc cref="IClawBack" />
    public class ClawBackResponse : TransactionResponse, IClawBack
    {
        #region Implementation of IClawBack

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string Holder { get; set; }

        #endregion
    }
    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of an ClawBack at runtime.
        /// </summary>
        /// <param name="tx">An ClawBack Transaction.</param>
        /// <returns></returns>
        /// <exception cref="ValidationException">When the ClawBack is Malformed.</exception>
        public static async Task ValidateClawBack(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Amount", out var Amount) || Amount is null)
            {
                throw new ValidationException("ClawBack: missing field Amount");
            }

            if (!Xrpl.Models.Transactions.Common.IsIssuedCurrency(Amount))
            {
                throw new ValidationException("ClawBack: invalid Amount");
            }

            if (!tx.TryGetValue("Account", out var acc) || acc is null)
                throw new ValidationException("ClawBack: invalid Account");
            var amountJson = JsonSerializer.Serialize(Amount, XrplJsonOptions.Default);
            var amount = JsonSerializer.Deserialize<Currency>(amountJson, XrplJsonOptions.Default);
            if (amount.Issuer == (string)acc)
            {
                throw new ValidationException("ClawBack: invalid holder Account");
            }
        }
    }

}
