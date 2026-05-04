using System.Text.Json.Serialization;

namespace Xrpl.Models.Transactions
{
    public class BinaryTransactionResponse : BaseTransactionResponse
    {
        [JsonPropertyName("meta")]
        public string Meta { get; set; }

        [JsonPropertyName("tx")]
        public string Transaction { get; set; }
    }
}
