using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Claw back tokens from an Automated Market Maker (AMM) pool.
    /// This transaction allows a token issuer to recover tokens that a holder
    /// has deposited into an AMM. The issuer must have enabled clawback on their
    /// account before issuing any tokens.
    /// </summary>
    public class AMMClawBack : TransactionRequest, IAMMClawBack
    {
        public AMMClawBack()
        {
            TransactionType = TransactionType.AMMClawback;
        }

        /// <inheritdoc />
        [JsonProperty("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset2 { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Xrpl.Models.Common.Currency Amount { get; set; }
    }

    /// <summary>
    /// Claw back tokens from an Automated Market Maker (AMM) pool.
    /// This transaction allows a token issuer to recover tokens that a holder
    /// has deposited into an AMM. The issuer must have enabled clawback on their
    /// account before issuing any tokens.
    /// </summary>
    public interface IAMMClawBack : ITransactionCommon
    {
        /// <summary>
        /// The account holding the asset to be clawed back.
        /// This is the holder who deposited tokens into the AMM pool.
        /// </summary>
        public string Holder { get; set; }

        /// <summary>
        /// Specifies one of the pool assets (XRP or token) of the AMM instance.
        /// Together with Asset2, this identifies which AMM pool to claw back from.
        /// </summary>
        public IssuedCurrency Asset { get; set; }

        /// <summary>
        /// Specifies the other pool asset of the AMM instance.
        /// Together with Asset, this identifies which AMM pool to claw back from.
        /// </summary>
        public IssuedCurrency Asset2 { get; set; }

        /// <summary>
        /// The amount of the asset to claw back from the AMM pool.
        /// If not provided, claws back the maximum possible amount.
        /// </summary>
        public Xrpl.Models.Common.Currency Amount { get; set; }
    }

    /// <inheritdoc cref="IAMMClawBack" />
    public class AMMClawBackResponse : TransactionResponse, IAMMClawBack
    {
        #region Implementation of IAMMClawBack

        /// <inheritdoc />
        [JsonProperty("Holder")]
        public string Holder { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset2 { get; set; }

        /// <inheritdoc />
        [JsonConverter(typeof(CurrencyConverter))]
        public Xrpl.Models.Common.Currency Amount { get; set; }

        #endregion
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of an AMMClawBack transaction at runtime.
        /// </summary>
        /// <param name="tx">An AMMClawBack Transaction.</param>
        /// <exception cref="ValidationException">When the AMMClawBack is malformed.</exception>
        public static async Task ValidateAMMClawBack(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Holder", out var Holder) || Holder is null)
            {
                throw new ValidationException("AMMClawback: missing field Holder");
            }

            tx.TryGetValue("Asset", out var Asset);
            tx.TryGetValue("Asset2", out var Asset2);

            if (Asset is null)
                throw new ValidationException("AMMClawback: missing field Asset");

            if (!Xrpl.Models.Transactions.Common.IsIssue(Asset))
                throw new ValidationException("AMMClawback: Asset must be an Issue");

            if (Asset2 is null)
                throw new ValidationException("AMMClawback: missing field Asset2");

            if (!Xrpl.Models.Transactions.Common.IsIssue(Asset2))
                throw new ValidationException("AMMClawback: Asset2 must be an Issue");

            if (tx.TryGetValue("Amount", out var Amount) && Amount is not null)
            {
                if (!Xrpl.Models.Transactions.Common.IsIssuedCurrency(Amount))
                {
                    throw new ValidationException("AMMClawback: invalid Amount");
                }
            }
        }
    }
}
