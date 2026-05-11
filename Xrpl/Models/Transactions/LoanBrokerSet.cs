using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanBrokerSet transaction creates or modifies a loan broker that manages lending pools.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanBrokerSet : ITransactionCommon
    {
        /// <summary>
        /// The primary asset managed by the loan broker.
        /// </summary>
        IssuedCurrency Asset { get; set; }

        /// <summary>
        /// The secondary asset (collateral) managed by the loan broker.
        /// </summary>
        IssuedCurrency Asset2 { get; set; }

        /// <summary>
        /// The minimum cover rate required for loans, as a percentage multiplied by 1000.
        /// </summary>
        uint? CoverRateMinimum { get; set; }

        /// <summary>
        /// The cover rate at which liquidation occurs, as a percentage multiplied by 1000.
        /// </summary>
        uint? CoverRateLiquidation { get; set; }

        /// <summary>
        /// The management fee rate charged by the broker.
        /// </summary>
        ushort? ManagementFeeRate { get; set; }

        /// <summary>
        /// The ID of a permissioned domain to associate with the loan broker.
        /// </summary>
        string DomainID { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerSet" />
    public class LoanBrokerSet : TransactionRequest, ILoanBrokerSet
    {
        public LoanBrokerSet()
        {
            TransactionType = TransactionType.LoanBrokerSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("Asset")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Asset2")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset2 { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CoverRateMinimum")]
        public uint? CoverRateMinimum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CoverRateLiquidation")]
        public uint? CoverRateLiquidation { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ManagementFeeRate")]
        public ushort? ManagementFeeRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerSet" />
    public class LoanBrokerSetResponse : TransactionResponse, ILoanBrokerSet
    {
        /// <inheritdoc />
        [JsonPropertyName("Asset")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Asset2")]
        [JsonConverter(typeof(IssuedCurrencyConverter))]
        public IssuedCurrency Asset2 { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CoverRateMinimum")]
        public uint? CoverRateMinimum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("CoverRateLiquidation")]
        public uint? CoverRateLiquidation { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("ManagementFeeRate")]
        public ushort? ManagementFeeRate { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanBrokerSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("Asset", out var asset) || asset is null)
                throw new ValidationException("LoanBrokerSet: missing field Asset");

            if (!tx.TryGetValue("Asset2", out var asset2) || asset2 is null)
                throw new ValidationException("LoanBrokerSet: missing field Asset2");
        }
    }
}
