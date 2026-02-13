#nullable enable
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.BinaryCodec.Types;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Utils;
using Xrpl.Utils;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Enum representing flags for MPTokenIssuanceCreate transactions.
    /// </summary>
    [Flags]
    public enum MPTokenIssuanceCreateFlags : uint
    {
        /// <summary>
        /// If set, indicates that the MPT can be locked both individually and globally.
        /// </summary>
        tfMPTCanLock = 2,

        /// <summary>
        /// If set, indicates that individual holders must be authorized.
        /// </summary>
        tfMPTRequireAuth = 4,

        /// <summary>
        /// If set, indicates that the MPT can be escrowed.
        /// </summary>
        tfMPTCanEscrow = 8,

        /// <summary>
        /// If set, indicates that the MPT can be traded on the DEX.
        /// </summary>
        tfMPTCanTrade = 16,

        /// <summary>
        /// If set, indicates that the MPT can be transferred between accounts.
        /// </summary>
        tfMPTCanTransfer = 32,

        /// <summary>
        /// If set, indicates that the issuer can clawback tokens.
        /// </summary>
        tfMPTCanClawback = 64
    }

    /// <summary>
    /// The MPTokenIssuanceCreate transaction creates an MPTokenIssuance object
    /// and adds it to the relevant directory node of the creator account.
    /// </summary>
    public interface IMPTokenIssuanceCreate : ITransactionCommon
    {
        /// <summary>
        /// An asset scale is the difference, in orders of magnitude, between a standard unit and
        /// a corresponding fractional unit.More formally, the asset scale is a non-negative integer
        /// (0, 1, 2, …) such that one standard unit equals 10^(-scale) of a corresponding
        /// fractional unit.If the fractional unit equals the standard unit, then the asset scale is 0.
        /// Note that this value is optional, and will default to 0 if not supplied.
        /// </summary>
        public uint? AssetScale { get; set; }

        /// <summary>
        /// Specifies the maximum asset amount of this token that should ever be issued.
        /// Valid values for this field are between 0 and 9223372036854775807 (the maximum signed 64-bit integer).
        /// If this value is not specified, the issuance does not have a limit.
        /// </summary>
        public string? MaximumAmount { get; set; }

        /// <summary>
        /// The value specifies the fee to charged by the issuer for secondary sales of the token,
        /// if such sales are allowed. Valid values for this field are between 0 and 50000 inclusive,
        /// allowing transfer rates of between 0.00% and 50.00% in increments of 0.001.
        /// </summary>
        public ushort? TransferFee { get; set; }

        /// <summary>
        /// Arbitrary metadata about this issuance, in hex format.
        /// The limit for this field is 1024 bytes.
        /// </summary>
        public string? MPTokenMetadata { get; set; }
        [JsonIgnore]
        public string? MPTokenMetadataRow => MPTokenMetadata?.FromHexString();

        /// <summary>
        /// Parsed metadata object conforming to the XLS-89 Multi-Purpose Token Metadata Schema.
        /// Setting this property automatically serializes the schema to the <see cref="MPTokenMetadata"/> hex field.
        /// </summary>
        [JsonIgnore]
        MPTokenMetadataSchema? Metadata
        {
            get => MPTokenMetadataSchema.FromHex(MPTokenMetadata);
            set => MPTokenMetadata = value?.ToHex();
        }

        public new MPTokenIssuanceCreateFlags? Flags { get; set; }
    }

    /// <summary>
    /// The MPTokenIssuanceCreate transaction creates an MPTokenIssuance object.
    /// </summary>
    public class MPTokenIssuanceCreate : TransactionRequest, IMPTokenIssuanceCreate
    {
        /// <summary>
        /// Initializes a new instance of the MPTokenIssuanceCreate class.
        /// </summary>
        public MPTokenIssuanceCreate()
        {
            TransactionType = TransactionType.MPTokenIssuanceCreate;
        }

        /// <inheritdoc />
        [JsonProperty("AssetScale")]
        public uint? AssetScale { get; set; }

        /// <inheritdoc />
        [JsonProperty("MaximumAmount")]
        public string? MaximumAmount { get; set; }

        /// <inheritdoc />
        [JsonProperty("TransferFee")]
        public ushort? TransferFee { get; set; }

        /// <inheritdoc />
        [JsonProperty("MPTokenMetadata")]
        public string? MPTokenMetadata { get; set; }

        [JsonIgnore]
        public string? MPTokenMetadataRow => MPTokenMetadata?.FromHexString();


        /// <inheritdoc />
        [JsonIgnore]
        public MPTokenMetadataSchema? Metadata
        {
            get => MPTokenMetadataSchema.FromHex(MPTokenMetadata);
            set => MPTokenMetadata = value?.ToHex();
        }

        public new MPTokenIssuanceCreateFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenIssuanceCreateFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    /// <inheritdoc cref="IMPTokenIssuanceCreate" />
    public class MPTokenIssuanceCreateResponse : TransactionResponse, IMPTokenIssuanceCreate
    {
        #region Implementation of IMPTokenIssuanceCreate

        /// <inheritdoc />
        [JsonProperty("AssetScale")]
        public uint? AssetScale { get; set; }

        /// <inheritdoc />
        [JsonProperty("MaximumAmount")]
        public string? MaximumAmount { get; set; }

        /// <inheritdoc />
        [JsonProperty("TransferFee")]
        public ushort? TransferFee { get; set; }

        /// <inheritdoc />
        private string? _mpTokenMetadata;

        [JsonProperty("MPTokenMetadata")]
        public string? MPTokenMetadata
        {
            get => _mpTokenMetadata;
            set { _mpTokenMetadata = value; _metadata = null; }
        }

        [JsonIgnore]
        public string? MPTokenMetadataRow => MPTokenMetadata?.FromHexString();

        private MPTokenMetadataSchema? _metadata;

        /// <inheritdoc />
        [JsonIgnore]
        public MPTokenMetadataSchema? Metadata
        {
            get => _metadata ??= MPTokenMetadataSchema.FromHex(MPTokenMetadata);
            set
            {
                _metadata = value;
                _mpTokenMetadata = value?.ToHex();
            }
        }

        #endregion
        public new MPTokenIssuanceCreateFlags? Flags
        {
            get => base.Flags.HasValue ? (MPTokenIssuanceCreateFlags?)base.Flags.Value : null;
            set => base.Flags = (uint?)value;
        }
    }

    public partial class Validation
    {
        /// <summary>
        /// Maximum transfer fee for MPT (50.00% = 50000).
        /// </summary>
        public const ushort MPT_MAX_TRANSFER_FEE = 50000;

        /// <summary>
        /// Maximum asset scale for MPT.
        /// </summary>
        public const byte MPT_MAX_ASSET_SCALE = 10;

        /// <summary>
        /// Maximum metadata length in bytes.
        /// </summary>
        public const int MPT_MAX_METADATA_LENGTH = 1024;

        /// <summary>
        /// Verify the form and type of an MPTokenIssuanceCreate at runtime.
        /// </summary>
        /// <param name="tx">An MPTokenIssuanceCreate Transaction.</param>
        /// <exception cref="ValidationException">When the MPTokenIssuanceCreate is Malformed.</exception>
        public static async Task ValidateMPTokenIssuanceCreate(Dictionary<string, dynamic> tx)
        {
            await Common.ValidateBaseTransaction(tx);

            if (tx.TryGetValue("AssetScale", out var assetScale) && assetScale is not null)
            {
                byte scale;
                try
                {
                    scale = Convert.ToByte(assetScale);
                }
                catch
                {
                    throw new ValidationException("MPTokenIssuanceCreate: AssetScale must be a number");
                }

                if (scale > MPT_MAX_ASSET_SCALE)
                {
                    throw new ValidationException($"MPTokenIssuanceCreate: AssetScale must be between 0 and {MPT_MAX_ASSET_SCALE}");
                }
            }

            if (tx.TryGetValue("TransferFee", out var transferFee) && transferFee is not null)
            {
                ushort fee;
                try
                {
                    fee = Convert.ToUInt16(transferFee);
                }
                catch
                {
                    throw new ValidationException("MPTokenIssuanceCreate: TransferFee must be a number");
                }

                if (fee > MPT_MAX_TRANSFER_FEE)
                {
                    throw new ValidationException($"MPTokenIssuanceCreate: TransferFee must be between 0 and {MPT_MAX_TRANSFER_FEE}");
                }
            }

            if (tx.TryGetValue("MaximumAmount", out var maxAmount) && maxAmount is not null)
            {
                if (maxAmount is not string)
                {
                    throw new ValidationException("MPTokenIssuanceCreate: MaximumAmount must be a string");
                }
            }

            if (tx.TryGetValue("MPTokenMetadata", out var metadata) && metadata is not null)
            {
                if (metadata is not string metadataStr)
                {
                    throw new ValidationException("MPTokenIssuanceCreate: MPTokenMetadata must be a string");
                }

                if (metadataStr.Length > MPT_MAX_METADATA_LENGTH * 2)
                {
                    throw new ValidationException($"MPTokenIssuanceCreate: MPTokenMetadata must be at most {MPT_MAX_METADATA_LENGTH} bytes");
                }
            }
        }
    }
}