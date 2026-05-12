using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// The LoanBrokerSet transaction creates or modifies a loan broker that manages lending pools.
    /// The submitting account must own the Vault specified by VaultID.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanBrokerSet : ITransactionCommon
    {
        /// <summary>
        /// The ID of the Vault ledger object that this loan broker is associated with.
        /// The account submitting this transaction must own the vault.
        /// Required when creating a new loan broker.
        /// </summary>
        string VaultID { get; set; }

        /// <summary>
        /// The ID of an existing LoanBroker object to modify.
        /// Optional — omit when creating a new loan broker.
        /// </summary>
        string LoanBrokerID { get; set; }

        /// <summary>
        /// The minimum cover rate required for loans (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? CoverRateMinimum { get; set; }

        /// <summary>
        /// The cover rate at which liquidation occurs (1/100th of a basis point).
        /// Valid range: 0–100000.
        /// </summary>
        uint? CoverRateLiquidation { get; set; }

        /// <summary>
        /// The management fee rate charged by the broker (1/10th of a basis point).
        /// Valid range: 0–10000.
        /// </summary>
        ushort? ManagementFeeRate { get; set; }

        /// <summary>
        /// The maximum amount the protocol can owe the Vault.
        /// Default 0 means no limit. Must not be negative.
        /// (Number type, string representation.)
        /// </summary>
        string DebtMaximum { get; set; }

        /// <summary>
        /// Arbitrary hex-encoded metadata, limited to 256 bytes.
        /// </summary>
        string Data { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerSet" />
    public class LoanBrokerSet : TransactionRequest, ILoanBrokerSet
    {
        public LoanBrokerSet()
        {
            TransactionType = TransactionType.LoanBrokerSet;
        }

        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

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
        [JsonPropertyName("DebtMaximum")]
        public string DebtMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }
    }

    /// <inheritdoc cref="ILoanBrokerSet" />
    public class LoanBrokerSetResponse : TransactionResponse, ILoanBrokerSet
    {
        /// <inheritdoc />
        [JsonPropertyName("VaultID")]
        public string VaultID { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("LoanBrokerID")]
        public string LoanBrokerID { get; set; }

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
        [JsonPropertyName("DebtMaximum")]
        public string DebtMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanBrokerSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("LoanBrokerSet: missing field VaultID");
        }
    }
}
