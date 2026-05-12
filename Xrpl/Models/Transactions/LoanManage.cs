using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Enums;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Flags for the LoanManage transaction. Mutually exclusive.
    /// </summary>
    [Flags]
    public enum LoanManageFlags : uint
    {
        /// <summary>
        /// batch inner transaction
        /// </summary>
        tfInnerBatchTxn = XrplGlobalFlags.tfInnerBatchTxn,

        /// <summary>
        /// Mark the loan as defaulted.
        /// </summary>
        tfLoanDefault = 0x00010000,

        /// <summary>
        /// Mark the loan as impaired.
        /// </summary>
        tfLoanImpair = 0x00020000,

        /// <summary>
        /// Remove impairment from the loan.
        /// </summary>
        tfLoanUnimpair = 0x00040000,
    }

    /// <summary>
    /// The LoanManage transaction manages a loan (e.g. default, impair, or unimpair).
    /// Flags are mutually exclusive.
    /// </summary>
    /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
    public interface ILoanManage : ITransactionCommon
    {
        /// <summary>
        /// LoanManage transaction flags.
        /// </summary>
        new LoanManageFlags? Flags { get; set; }

        /// <summary>
        /// The ID of the loan to manage.
        /// </summary>
        string LoanID { get; set; }
    }

    /// <inheritdoc cref="ILoanManage" />
    public class LoanManage : TransactionRequest, ILoanManage
    {
        public LoanManage()
        {
            TransactionType = TransactionType.LoanManage;
        }

        /// <inheritdoc />
        public new LoanManageFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanManageFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }
    }

    /// <inheritdoc cref="ILoanManage" />
    public class LoanManageResponse : TransactionResponse, ILoanManage
    {
        /// <inheritdoc />
        public new LoanManageFlags? Flags
        {
            get => base.Flags.HasValue ? (LoanManageFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

        /// <inheritdoc />
        [JsonPropertyName("LoanID")]
        public string LoanID { get; set; }
    }

    public partial class Validation
    {
        public static async Task ValidateLoanManage(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("LoanID", out var id) || id is not string)
                throw new ValidationException("LoanManage: missing field LoanID");

            // tfLoanImpair and tfLoanUnimpair are mutually exclusive (per xrpl.js)
            if (tx.TryGetValue("Flags", out var flagsObj) && flagsObj is not null)
            {
                uint rawFlags = ParseFlags(flagsObj);
                bool hasImpair = (rawFlags & (uint)LoanManageFlags.tfLoanImpair) != 0;
                bool hasUnimpair = (rawFlags & (uint)LoanManageFlags.tfLoanUnimpair) != 0;
                if (hasImpair && hasUnimpair)
                    throw new ValidationException("LoanManage: tfLoanImpair and tfLoanUnimpair are mutually exclusive");
            }
        }

        private static uint ParseFlags(object flagsObj)
        {
            return flagsObj switch
            {
                uint u => u,
                int i when i >= 0 => (uint)i,
                long l when l is >= 0 and <= uint.MaxValue => (uint)l,
                JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetUInt32(out uint u) => u,
                _ => 0
            };
        }
    }
}
