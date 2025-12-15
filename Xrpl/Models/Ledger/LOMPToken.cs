using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/ledger/MPToken.ts

namespace Xrpl.Models.Ledger
{
    [Flags]
    public enum MPTokenFlags : uint
    {
        /// <summary>
        /// If enabled, indicates that the MPT owned by this account is currently locked and cannot be used in any XRP transactions other than sending value back to the issuer.
        /// </summary>
        lsfMPTLocked = 1,
        /// <summary>
        /// (Only applicable for allow-listing) If set, indicates that the issuer has authorized the holder for the MPT.<br/>
        /// This flag can be set using a MPTokenAuthorize transaction;<br/>
        /// it can also be "un-set" using a MPTokenAuthorize transaction specifying the tfMPTUnauthorize flag.
        /// </summary>
        lsfMPTAuthorized = 2,
    }
    /// <summary>
    /// The MPToken object represents an amount of an MPT held by an account that is not the issuer.
    /// </summary>
    public class LOMPToken : BaseLedgerEntry
    {
        public LOMPToken()
        {
            LedgerEntryType = LedgerEntryType.MPToken;
        }

        /// <summary>
        /// A bit-map of boolean flags enabled for this MPToken.
        /// </summary>
        public MPTokenFlags? Flags { get; set; }
        /// <summary>
        /// Owner (holder) of these MPTs.
        /// AccountID
        /// </summary>
        [JsonProperty("Account")]
        public string Account { get; init; } = default!;

        /// <summary>
        /// MPTokenIssuance identifier.
        /// UInt192 (hex)
        /// </summary>
        [JsonProperty("MPTokenIssuanceID")]
        public string MPTokenIssuanceID { get; init; } = default!;

        /// <summary>
        /// Amount of tokens currently held by the owner.
        /// UInt64 (0 .. 2^63-1)
        /// </summary>
        [JsonProperty("MPTAmount")]
        [JsonConverter(typeof(UInt64StringJsonConverter))]
        public ulong? MPTAmount { get; init; }

        /// <summary>
        /// Amount of tokens currently locked (included in MPTAmount).
        /// UInt64 (0 .. 2^63-1)
        /// Requires TokenEscrow amendment.
        /// Optional
        /// </summary>
        [JsonProperty("LockedAmount")]
        [JsonConverter(typeof(UInt64StringJsonConverter))]
        public ulong? LockedAmount { get; init; }

        /// <summary>
        /// Hash of the transaction that last modified this entry.
        /// UInt256
        /// </summary>
        [JsonProperty("PreviousTxnID")]
        public string? PreviousTxnID { get; init; }

        /// <summary>
        /// Ledger index of the previous modifying transaction.
        /// UInt32
        /// </summary>
        [JsonProperty("PreviousTxnLgrSeq")]
        public uint? PreviousTxnLgrSeq { get; init; }

        /// <summary>
        /// Owner directory page hint.
        /// UInt64 (hex)
        /// </summary>
        [JsonProperty("OwnerNode")]
        [JsonConverter(typeof(UInt64HexJsonConverter))]
        public ulong? OwnerNode { get; init; }
    }
}
