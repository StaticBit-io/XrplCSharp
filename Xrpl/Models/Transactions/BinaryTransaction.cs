using System.Text.Json.Serialization;

namespace Xrpl.Models.Transactions
{
    public class BinaryTransaction
    {
        [JsonPropertyName("meta")]
        public string Meta { get; set; }

        [JsonPropertyName("tx_blob")]
        public string TransactionBlob { get; set; }
    }
}
