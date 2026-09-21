// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/ledger/FeeSettings.ts

using System.Text.Json.Serialization;

namespace Xrpl.Models.Ledger
{
    /// <summary>
    /// The FeeSettings object type contains the current base transaction cost and reserve amounts as determined by fee voting.
    /// </summary>
    public class LOFeeSettings : BaseLedgerEntry
    {
        /// <summary>
        /// A bit-map of boolean flags for this object.<br/>
        /// No flags are defined for this type
        /// </summary>
        public uint? Flags { get; set; }

        /// <summary>
        /// The transaction cost of the "reference transaction" in drops of XRP as hexadecimal.
        /// </summary>
        public string BaseFee { get; set; }
        /// <summary>
        /// The BaseFee translated into "fee units".
        /// </summary>
        public uint? ReferenceFeeUnits { get; set; }
        /// <summary>
        /// The base reserve for an account in the XRP Ledger, as drops of XRP.
        /// </summary>
        public uint? ReserveBase { get; set; }
        /// <summary>
        /// The incremental owner reserve for owning objects, as drops of XRP.
        /// </summary>
        public uint? ReserveIncrement { get; set; }
    
        /// <summary>XRPFees: base fee in drops.</summary>
        [JsonPropertyName("BaseFeeDrops")]
        public string BaseFeeDrops { get; set; }

        /// <summary>XRPFees: account reserve in drops.</summary>
        [JsonPropertyName("ReserveBaseDrops")]
        public string ReserveBaseDrops { get; set; }

        /// <summary>XRPFees: owner reserve increment in drops.</summary>
        [JsonPropertyName("ReserveIncrementDrops")]
        public string ReserveIncrementDrops { get; set; }

        /// <summary>
        /// The identifying hash of the transaction that most recently modified this object.
        /// </summary>
        [JsonPropertyName("PreviousTxnID")]
        public string PreviousTxnID { get; set; }

        /// <summary>
        /// SmartEscrow: the most gas one escrow execution may consume, set by fee voting rather than
        /// by amendment so the network can raise it as the engine improves. Voting it to zero
        /// disables Smart Escrows without touching the amendment.
        /// </summary>
        [JsonPropertyName("GasLimit")]
        public uint? GasLimit { get; set; }

        /// <summary>
        /// SmartEscrow: the largest <c>Bytecode</c> an escrow may carry, in bytes. Voted the same way
        /// as <see cref="GasLimit"/>, and zero disables Smart Escrows the same way.
        /// </summary>
        [JsonPropertyName("BytecodeSizeLimit")]
        public uint? BytecodeSizeLimit { get; set; }

        /// <summary>
        /// SmartEscrow: the price of one gas unit, in millionths of a drop.
        /// </summary>
        [JsonPropertyName("GasPrice")]
        public uint? GasPrice { get; set; }

        /// <summary>
        /// The index of the ledger that contains the transaction that most recently modified this object.
        /// </summary>
        [JsonPropertyName("PreviousTxnLgrSeq")]
        public uint? PreviousTxnLgrSeq { get; set; }
}
}
