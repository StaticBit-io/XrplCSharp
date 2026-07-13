#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Utils;
using Xrpl.Utils;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// DynamicMPT (XLS-94): MutableFlags values for MPTokenIssuanceSet —
    /// one-way enabling of capability flags (once enabled, cannot be disabled here).
    /// Values mirror rippled TxFlags.h tmfMPTSet*.
    /// </summary>
    [Flags]
    public enum MPTokenIssuanceSetMutableFlags : uint
    {
        /// <summary>Enable lsfMPTCanLock on the issuance.</summary>
        tmfMPTSetCanLock = 0x00000001,

        /// <summary>Enable lsfMPTRequireAuth on the issuance.</summary>
        tmfMPTSetRequireAuth = 0x00000002,

        /// <summary>Enable lsfMPTCanEscrow on the issuance.</summary>
        tmfMPTSetCanEscrow = 0x00000004,

        /// <summary>Enable lsfMPTCanTrade on the issuance.</summary>
        tmfMPTSetCanTrade = 0x00000008,

        /// <summary>Enable lsfMPTCanTransfer on the issuance.</summary>
        tmfMPTSetCanTransfer = 0x00000010,

        /// <summary>Enable lsfMPTCanClawback on the issuance.</summary>
        tmfMPTSetCanClawback = 0x00000020,

        /// <summary>Enable holding confidential balances (ConfidentialTransfer).</summary>
        tmfMPTSetCanHoldConfidentialBalance = 0x00000040,
    }

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
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string? Holder { get; set; }
        public new MPTokenIssuanceSetFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenIssuanceSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

    
        /// <summary>DynamicMPT: capability flags to enable on the issuance (one-way).</summary>
        [JsonPropertyName("MutableFlags")]
        public MPTokenIssuanceSetMutableFlags? MutableFlags { get; set; }

        /// <summary>DynamicMPT: new transfer fee (requires tfMPTCanMutateTransferFee).</summary>
        [JsonPropertyName("TransferFee")]
        public ushort? TransferFee { get; set; }

        /// <summary>DynamicMPT: new metadata blob in hex (requires tfMPTCanMutateMetadata).</summary>
        [JsonPropertyName("MPTokenMetadata")]
        public string MPTokenMetadata { get; set; }

        /// <summary>Decoded (non-hex) representation of <see cref="MPTokenMetadata"/>.</summary>
        [JsonIgnore]
        public string? MPTokenMetadataRow => MPTokenMetadata?.FromHexString();

        /// <summary>
        /// Parsed metadata object conforming to the XLS-89 Multi-Purpose Token Metadata Schema.
        /// Setting this property automatically serializes the schema to the <see cref="MPTokenMetadata"/> hex field.
        /// </summary>
        [JsonIgnore]
        public MPTokenMetadataSchema? Metadata
        {
            get => MPTokenMetadataSchema.FromHex(MPTokenMetadata);
            set => MPTokenMetadata = value?.ToHex();
        }

        /// <summary>PermissionedDomains: domain restricting who may hold this MPT.</summary>
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }

        /// <summary>ConfidentialTransfer: issuer ElGamal encryption public key (hex).</summary>
        [JsonPropertyName("IssuerEncryptionKey")]
        public string IssuerEncryptionKey { get; set; }

        /// <summary>ConfidentialTransfer: auditor ElGamal encryption public key (hex).</summary>
        [JsonPropertyName("AuditorEncryptionKey")]
        public string AuditorEncryptionKey { get; set; }
}

    /// <inheritdoc cref="IMPTokenIssuanceSet" />
    public class MPTokenIssuanceSetResponse : TransactionResponse, IMPTokenIssuanceSet
    {
        #region Implementation of IMPTokenIssuanceSet

        /// <inheritdoc />
        [JsonPropertyName("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; set; } = null!;

        /// <inheritdoc />
        [JsonPropertyName("Holder")]
        public string? Holder { get; set; }

        #endregion
        public new MPTokenIssuanceSetFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenIssuanceSetFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }

    
        /// <summary>DynamicMPT: capability flags to enable on the issuance (one-way).</summary>
        [JsonPropertyName("MutableFlags")]
        public MPTokenIssuanceSetMutableFlags? MutableFlags { get; set; }

        /// <summary>DynamicMPT: new transfer fee (requires tfMPTCanMutateTransferFee).</summary>
        [JsonPropertyName("TransferFee")]
        public ushort? TransferFee { get; set; }

        /// <summary>DynamicMPT: new metadata blob in hex (requires tfMPTCanMutateMetadata).</summary>
        [JsonPropertyName("MPTokenMetadata")]
        public string MPTokenMetadata { get; set; }

        /// <summary>Decoded (non-hex) representation of <see cref="MPTokenMetadata"/>.</summary>
        [JsonIgnore]
        public string? MPTokenMetadataRow => MPTokenMetadata?.FromHexString();

        /// <summary>
        /// Parsed metadata object conforming to the XLS-89 Multi-Purpose Token Metadata Schema.
        /// </summary>
        [JsonIgnore]
        public MPTokenMetadataSchema? Metadata => MPTokenMetadataSchema.FromHex(MPTokenMetadata);

        /// <summary>PermissionedDomains: domain restricting who may hold this MPT.</summary>
        [JsonPropertyName("DomainID")]
        public string DomainID { get; set; }

        /// <summary>ConfidentialTransfer: issuer ElGamal encryption public key (hex).</summary>
        [JsonPropertyName("IssuerEncryptionKey")]
        public string IssuerEncryptionKey { get; set; }

        /// <summary>ConfidentialTransfer: auditor ElGamal encryption public key (hex).</summary>
        [JsonPropertyName("AuditorEncryptionKey")]
        public string AuditorEncryptionKey { get; set; }
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