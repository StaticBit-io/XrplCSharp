#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Enum representing flags for MPTokenIssuanceSet transactions.
    /// </summary>
    [Flags]
    public enum MPTokenIssuanceSetFlags : uint
    {
        /// <summary>
        /// If set, indicates that all MPT balances for this asset should be locked.
        /// </summary>
        tfMPTLock = 1,

        /// <summary>
        /// If set, indicates that all MPT balances for this asset should be unlocked.
        /// </summary>
        tfMPTUnlock = 2
    }

    /// <summary>
    /// The MPTokenIssuanceSet transaction is used to globally lock/unlock an MPTokenIssuance,
    /// or to lock/unlock a specific holder's MPToken balance for an MPTokenIssuance.
    /// </summary>
    public interface IMPTokenIssuanceSet : ITransactionCommon
    {
        /// <summary>
        /// The MPTokenIssuance identifier.
        /// </summary>
        public string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// An optional XRPL Address of an individual token holder balance to lock/unlock.
        /// If omitted, this transaction will apply to all any accounts holding MPTs.
        /// </summary>
        public string? Holder { get; set; }
        public new MPTokenIssuanceSetFlags? Flags { get; set; }

    }

    /// <summary>
    /// The MPTokenIssuanceSet transaction is used to globally lock/unlock an MPTokenIssuance.
    /// </summary>
    public class MPTokenIssuanceSet : TransactionRequest, IMPTokenIssuanceSet
    {
        /// <summary>
        /// Initializes a new instance of the MPTokenIssuanceSet class.
        /// </summary>
        public MPTokenIssuanceSet()
        {
            TransactionType = TransactionType.MPTokenIssuanceSet;
        }

        /// <inheritdoc />
        [JsonProperty("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        /// <inheritdoc />
        [JsonProperty("Holder")]
        public string? Holder { get; set; }
        public new MPTokenIssuanceSetFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenIssuanceSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

    }

    /// <inheritdoc cref="IMPTokenIssuanceSet" />
    public class MPTokenIssuanceSetResponse : TransactionResponse, IMPTokenIssuanceSet
    {
        #region Implementation of IMPTokenIssuanceSet

        /// <inheritdoc />
        [JsonProperty("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        /// <inheritdoc />
        [JsonProperty("Holder")]
        public string? Holder { get; set; }

        #endregion
        public new MPTokenIssuanceSetFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenIssuanceSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of an MPTokenIssuanceSet at runtime.
        /// </summary>
        /// <param name="tx">An MPTokenIssuanceSet Transaction.</param>
        /// <exception cref="ValidationException">When the MPTokenIssuanceSet is Malformed.</exception>
        public static async Task ValidateMPTokenIssuanceSet(Dictionary<string, object> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("MPTokenIssuanceID", out var issuanceId) || issuanceId is null)
            {
                throw new ValidationException("MPTokenIssuanceSet: missing field MPTokenIssuanceID");
            }

            if (issuanceId is not string)
            {
                throw new ValidationException("MPTokenIssuanceSet: MPTokenIssuanceID must be a string");
            }

            if (tx.TryGetValue("Holder", out var holder) && holder is not null)
            {
                if (holder is not string)
                {
                    throw new ValidationException("MPTokenIssuanceSet: Holder must be a string");
                }
            }

            if (tx.TryGetValue("Flags", out var flags) && flags is not null)
            {
                uint flagValue = Convert.ToUInt32(flags);
                bool hasLock = (flagValue & (uint)MPTokenIssuanceSetFlags.tfMPTLock) != 0;
                bool hasUnlock = (flagValue & (uint)MPTokenIssuanceSetFlags.tfMPTUnlock) != 0;

                if (hasLock && hasUnlock)
                {
                    throw new ValidationException("MPTokenIssuanceSet: cannot set both tfMPTLock and tfMPTUnlock flags");
                }
            }
        }
    }
}