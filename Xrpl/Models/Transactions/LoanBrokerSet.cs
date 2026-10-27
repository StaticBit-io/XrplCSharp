using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;

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
        /// The share of the broker's outstanding debt that first-loss capital must cover,
        /// in 1/10th of a basis point: 100000 = 100%, 5000 = 5%. Valid range: 0–100000.
        /// </summary>
        uint? CoverRateMinimum { get; set; }

        /// <summary>
        /// The share of the minimum required first-loss capital moved to the vault to cover a loan default,
        /// in 1/10th of a basis point: 100000 = 100%, 5000 = 5%. Valid range: 0–100000.
        /// </summary>
        uint? CoverRateLiquidation { get; set; }

        /// <summary>
        /// The management fee rate charged by the broker, in 1/10th of a basis point: 10000 = 10%, 500 = 0.5%.
        /// Valid range: 0–10000.
        /// </summary>
        ushort? ManagementFeeRate { get; set; }

        /// <summary>
        /// The maximum amount the protocol can owe the Vault.
        /// Default 0 means no limit. Must not be negative.
        /// (Number type.)
        /// </summary>
        XrplNumber? DebtMaximum { get; set; }

        /// <summary>
        /// Arbitrary hex-encoded metadata, limited to 256 bytes.
        /// </summary>
        string Data { get; set; }

        /// <summary>
        /// (Optional) LendingProtocolV1_2: the permissioned domain a private loan broker is bound to.
        /// Only a broker created with <see cref="LoanBrokerSetFlags.tfLoanBrokerPrivate"/> may carry
        /// one; on an existing private broker the zero hash removes it.
        /// </summary>
        string DomainID { get; set; }
    }

    /// <summary>
    /// Flags of <see cref="LoanBrokerSet"/> (rippled TxFlags.h).
    /// </summary>
    [System.Flags]
    public enum LoanBrokerSetFlags : uint
    {
        /// <summary>
        /// LendingProtocolV1_2: create a private loan broker, which may be bound to a permissioned domain.
        /// </summary>
        tfLoanBrokerPrivate = 0x00010000,
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
        public XrplNumber? DebtMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string DomainID { get; set; }

        /// <summary>
        /// Typed view over the base Flags value (see <see cref="LoanBrokerSetFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new LoanBrokerSetFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanBrokerSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
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
        [JsonConverter(typeof(LenientXrplNumberConverter))]
        public XrplNumber? DebtMaximum { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("Data")]
        public string Data { get; set; }

        /// <inheritdoc />
        [JsonPropertyName("DomainID")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string DomainID { get; set; }

        /// <summary>
        /// Typed view over the base Flags value (see <see cref="LoanBrokerSetFlags"/>).
        /// </summary>
        [JsonPropertyName("Flags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public new LoanBrokerSetFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanBrokerSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    public partial class Validation
    {
        public static void ValidateLoanBrokerSet(Dictionary<string, object> tx)
        {
            Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("VaultID", out var vaultId) || vaultId is not string)
                throw new ValidationException("LoanBrokerSet: missing field VaultID");

            // Mirror of rippled LoanBrokerSet::preflight for DomainID on a new broker; on an
            // existing broker the zero hash is how DomainID is removed.
            if (tx.TryGetValue("DomainID", out var domainId))
            {
                if (domainId is not string domain)
                    throw new ValidationException("LoanBrokerSet: invalid DomainID");
                if (!tx.ContainsKey("LoanBrokerID"))
                {
                    if (string.Equals(domain, ZeroHash256, System.StringComparison.Ordinal))
                        throw new ValidationException("LoanBrokerSet: DomainID must not be zero when creating a loan broker");
                    if ((ExtractFlags(tx) & (uint)LoanBrokerSetFlags.tfLoanBrokerPrivate) == 0)
                        throw new ValidationException("LoanBrokerSet: a public loan broker cannot have a DomainID");
                }
            }
        }
    }
}
