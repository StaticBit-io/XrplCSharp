//https://xrpl.org/setfee.html
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

namespace Xrpl.Models.Transactions
{
    public class SetFee : TransactionRequest, ISetFee
    {
        public SetFee()
        {
            TransactionType = TransactionType.SetFee;
        }

        /// <inheritdoc />
        public string BaseFee { get; set; }

        /// <inheritdoc />
        public uint ReferenceFeeUnits { get; set; }

        /// <inheritdoc />
        public uint ReserveBase { get; set; }

        /// <inheritdoc />
        public uint ReserveIncrement { get; set; }

        /// <inheritdoc />
        public uint LedgerSequence { get; set; }
    
        /// <summary>XRPFees: base fee in drops.</summary>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency BaseFeeDrops { get; set; }

        /// <summary>XRPFees: account reserve in drops.</summary>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency ReserveBaseDrops { get; set; }

        /// <summary>XRPFees: owner reserve increment in drops.</summary>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency ReserveIncrementDrops { get; set; }
}

    public interface ISetFee : ITransactionCommon
    {
        /// <summary>
        /// The charge, in drops of XRP, for the reference transaction, as hex.<br/>
        /// (This is the transaction cost before scaling for load.)
        /// </summary>
        string BaseFee { get; set; }
        /// <summary>
        /// (Omitted for some historical SetFee pseudo-transactions)<br/>
        /// The index of the ledger version where this pseudo-transaction appears.<br/>
        /// This distinguishes the pseudo-transaction from other occurrences of the same change.
        /// </summary>
        uint LedgerSequence { get; set; }
        /// <summary>
        /// The cost, in fee units, of the reference transaction
        /// </summary>
        uint ReferenceFeeUnits { get; set; }
        /// <summary>
        /// The base reserve, in drops
        /// </summary>
        uint ReserveBase { get; set; }
        /// <summary>
        /// The incremental reserve, in drops
        /// </summary>
        uint ReserveIncrement { get; set; }
    }

    public class SetFeeResponse : TransactionResponse, ISetFee
    {
        /// <inheritdoc />
        public string BaseFee { get; set; }
        /// <inheritdoc />
        public uint LedgerSequence { get; set; }
        /// <inheritdoc />
        public uint ReferenceFeeUnits { get; set; }
        /// <inheritdoc />
        public uint ReserveBase { get; set; }
        /// <inheritdoc />
        public uint ReserveIncrement { get; set; }
    
        /// <summary>XRPFees: base fee in drops.</summary>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency BaseFeeDrops { get; set; }

        /// <summary>XRPFees: account reserve in drops.</summary>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency ReserveBaseDrops { get; set; }

        /// <summary>XRPFees: owner reserve increment in drops.</summary>
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency ReserveIncrementDrops { get; set; }
}
}
