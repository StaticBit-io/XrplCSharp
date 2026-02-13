using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Utils;
using Xrpl.Utils;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/ledger/MPTokenIssuance.ts

namespace Xrpl.Models.Ledger
{
    [Flags]
    public enum MPTokenIssuanceFlags : uint
    {
        None = 0,

        /// <summary>
        /// All balances of this MPT are locked.
        /// </summary>
        MPTLocked = 0x00000001,

        /// <summary>
        /// Issuer can lock individual balances or all balances.
        /// </summary>
        MPTCanLock = 0x00000002,

        /// <summary>
        /// Holders must be authorized by the issuer.
        /// </summary>
        MPTRequireAuth = 0x00000004,

        /// <summary>
        /// Holders can place balances into escrow.
        /// Requires TokenEscrow amendment.
        /// </summary>
        MPTCanEscrow = 0x00000008,

        /// <summary>
        /// Holders can trade balances via DEX or AMM.
        /// </summary>
        MPTCanTrade = 0x00000010,

        /// <summary>
        /// Tokens held by non-issuers can be transferred to other accounts.
        /// </summary>
        MPTCanTransfer = 0x00000020,

        /// <summary>
        /// Issuer can claw back balances from holders.
        /// </summary>
        MPTCanClawback = 0x00000040,
    }

    /// <summary>
    /// The MPTokenIssuance object represents a Multi-Purpose Token (MPT) issuance.
    /// </summary>
    public class LOMPTokenIssuance : BaseLedgerEntry
    {
        public LOMPTokenIssuance()
        {
            LedgerEntryType = LedgerEntryType.MPTokenIssuance;
        }

        [JsonProperty("Flags")]
        public MPTokenIssuanceFlags? Flags { get; init; }

        /// <summary>
        /// The address of the account that controls the issuance.
        /// AccountID
        /// </summary>
        [JsonProperty("Issuer")]
        public string Issuer { get; init; } = default!;

        /// <summary>
        /// Asset scale (decimal places).
        /// UInt8
        /// </summary>
        [JsonProperty("AssetScale")]
        public byte AssetScale { get; init; }

        /// <summary>
        /// Maximum number of tokens that can exist.
        /// UInt64 (0 .. 2^63-1)
        /// Optional
        /// </summary>
        [JsonProperty("MaximumAmount")]
        [JsonConverter(typeof(UInt64StringJsonConverter))]
        public ulong? MaximumAmount { get; init; }

        /// <summary>
        /// Total amount of tokens currently in circulation.
        /// UInt64 (0 .. 2^63-1)
        /// </summary>
        [JsonProperty("OutstandingAmount")]
        [JsonConverter(typeof(UInt64StringJsonConverter))]
        public ulong OutstandingAmount { get; init; }

        /// <summary>
        /// Amount of tokens currently locked (included in OutstandingAmount).
        /// UInt64 (0 .. 2^63-1)
        /// Requires TokenEscrow amendment.
        /// Optional
        /// </summary>
        [JsonProperty("LockedAmount")]
        [JsonConverter(typeof(UInt64StringJsonConverter))]
        public ulong? LockedAmount { get; init; }

        /// <summary>
        /// Transfer fee in tenths of a basis point.
        /// UInt16 (0 .. 50000)
        /// </summary>
        [JsonProperty("TransferFee")]
        public ushort? TransferFee { get; init; }

        /// <summary>
        /// Arbitrary metadata in hex format (max 1024 bytes).
        /// Blob
        /// </summary>
        [JsonProperty("MPTokenMetadata")]
        public string? MPTokenMetadata { get; init; }

        [JsonIgnore]
        public string? MPTokenMetadataRow => MPTokenMetadata?.FromHexString();


        /// <summary>
        /// Parsed metadata object conforming to the XLS-89 Multi-Purpose Token Metadata Schema.
        /// Lazily deserialized from the <see cref="MPTokenMetadata"/> hex field.
        /// </summary>
        [JsonIgnore]
        public MPTokenMetadataSchema? Metadata=> MPTokenMetadataSchema.FromHex(MPTokenMetadata);

        /// <summary>
        /// Owner directory page hint.
        /// UInt64
        /// </summary>
        [JsonProperty("OwnerNode")]
        [JsonConverter(typeof(UInt64HexJsonConverter))]
        public ulong? OwnerNode { get; init; }

        /// <summary>
        /// Hash of the transaction that last modified this entry.
        /// UInt256
        /// </summary>
        [JsonProperty("PreviousTxnID")]
        public string PreviousTxnID { get; init; } = default!;

        /// <summary>
        /// Ledger index of the previous modifying transaction.
        /// UInt32
        /// </summary>
        [JsonProperty("PreviousTxnLgrSeq")]
        public uint PreviousTxnLgrSeq { get; init; }

        /// <summary>
        /// Sequence or Ticket number that created this issuance.
        /// UInt32
        /// </summary>
        [JsonProperty("Sequence")]
        public uint Sequence { get; init; }
    }
}