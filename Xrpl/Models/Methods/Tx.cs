using System.Text.Json.Serialization;
//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/tx.ts
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// The tx method retrieves information on a single transaction, by its  identifying hash.<br/>
    /// Expects a response in the form of a TxResponse.
    /// </summary>
    public class TxRequest : BaseRequest
    {
        public TxRequest(string hash)
        {
            Command = "tx";
            Transaction = hash;
        }

        /// <summary>
        /// The 256-bit hash of the transaction to look up, as hexadecimal.
        /// </summary>
        [JsonPropertyName("transaction")]
        public string? Transaction { get; set; }

        /// <summary>
        /// The compact transaction identifier of the transaction to look up.<br/>
        /// Must use uppercase hexadecimal only.<br/>
        /// New in: rippled 1.12.0 (Not supported in Clio v2.0 and earlier)
        /// </summary>
        [JsonPropertyName("ctid")]
        public string? CtId { get; set; }

        /// <summary>
        /// If true, return transaction data and metadata as binary serialized to hexadecimal strings.<br/>
        /// If false, return transaction data and metadata as JSON.<br/>
        /// The default is false.
        /// </summary>
        [JsonPropertyName("binary")]
        public bool? Binary { get; set; }

        /// <summary>
        /// Use this with max_ledger to specify a range of up to 1000 ledger indexes, starting with this ledger (inclusive).<br/>
        /// If the server cannot find the transaction,<br/>
        /// it confirms whether it was able to search all the ledgers in this range.
        /// </summary>
        [JsonPropertyName("min_ledger")]
        public uint? MinLedger { get; set; }

        /// <summary>
        /// Use this with min_ledger to specify a range of up to 1000 ledger indexes, ending with this ledger (inclusive).<br/>
        /// If the server cannot find the transaction,<br/>
        /// it confirms whether it was able to search all the ledgers in the requested range.
        /// </summary>
        [JsonPropertyName("max_ledger")]
        public uint? MaxLedger { get; set; }
    }
    // todo not found class TxResponse extends BaseResponse
    //https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/methods/tx.ts#L41
}
