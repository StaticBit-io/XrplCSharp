#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client.Exceptions;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Enum representing flags for MPTokenAuthorize transactions.
    /// </summary>
    [Flags]
    public enum MPTokenAuthorizeFlags : uint
    {
        /// <summary>
        /// When the holder enables this flag, if their balance of the given MPT is zero,<br/>
        /// it revokes their willingness to hold this MPT and deletes their MPToken entry.<br/>
        /// If their balance is non-zero, the transaction fails.<br/>
        /// When an issuer enables this flag, it revokes permission for the specified holder to hold this MPT;<br/>
        /// the transaction fails if the MPT does not use allow-listing.
        /// </summary>
        tfMPTUnauthorize = 1
    }

    /// <summary>
    /// The MPTokenAuthorize transaction is used to allow an account to hold a particular MPT issuance,
    /// or by an issuer to authorize or revoke authorization for a holder.
    /// </summary>
    public interface IMPTokenAuthorize : ITransactionCommon
    {
        /// <summary>
        /// The ID of the MPT to authorize.
        /// </summary>
        public string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// An optional XRPL Address of an individual token holder balance.
        /// If this is set, the Holder field must specify the holder's address.
        /// If this is not set, the transaction is assumed to be from the holder directly.
        /// </summary>
        public string? Holder { get; set; }

        public new MPTokenAuthorizeFlags? Flags { get; set; }
    }

    /// <summary>
    /// The MPTokenAuthorize transaction authorizes an account to hold an MPT.
    /// </summary>
    public class MPTokenAuthorize : TransactionRequest, IMPTokenAuthorize
    {
        /// <summary>
        /// Initializes a new instance of the MPTokenAuthorize class.
        /// </summary>
        public MPTokenAuthorize()
        {
            TransactionType = TransactionType.MPTokenAuthorize;
        }

        /// <inheritdoc />
        [JsonProperty("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        /// <inheritdoc />
        [JsonProperty("Holder")]
        public string? Holder { get; set; }
        public new MPTokenAuthorizeFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenAuthorizeFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    /// <inheritdoc cref="IMPTokenAuthorize" />
    public class MPTokenAuthorizeResponse : TransactionResponse, IMPTokenAuthorize
    {
        #region Implementation of IMPTokenAuthorize

        /// <inheritdoc />
        [JsonProperty("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        /// <inheritdoc />
        [JsonProperty("Holder")]
        public string? Holder { get; set; }

        #endregion
        public new MPTokenAuthorizeFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenAuthorizeFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    public partial class Validation
    {
        /// <summary>
        /// Verify the form and type of an MPTokenAuthorize at runtime.
        /// </summary>
        /// <param name="tx">An MPTokenAuthorize Transaction.</param>
        /// <exception cref="ValidationException">When the MPTokenAuthorize is Malformed.</exception>
        public static async Task ValidateMPTokenAuthorize(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (!tx.TryGetValue("MPTokenIssuanceID", out var issuanceId) || issuanceId is null)
            {
                throw new ValidationException("MPTokenAuthorize: missing field MPTokenIssuanceID");
            }

            if (issuanceId is not string)
            {
                throw new ValidationException("MPTokenAuthorize: MPTokenIssuanceID must be a string");
            }

            if (tx.TryGetValue("Holder", out var holder) && holder is not null)
            {
                if (holder is not string)
                {
                    throw new ValidationException("MPTokenAuthorize: Holder must be a string");
                }
            }
        }
    }
}