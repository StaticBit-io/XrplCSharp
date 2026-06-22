using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

public sealed class PaymentResponseEnvelope
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("transaction")] public string? Transaction { get; set; }
    [JsonPropertyName("network")] public string? Network { get; set; }
    [JsonPropertyName("payer")] public string? Payer { get; set; }
    [JsonPropertyName("errorReason")] public string? ErrorReason { get; set; }
}
