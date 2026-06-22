using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

public sealed class SignedPayload
{
    [JsonPropertyName("signedTxBlob")] public string SignedTxBlob { get; set; } = "";
}

public sealed class PaymentSignatureEnvelope
{
    [JsonPropertyName("x402Version")] public int X402Version { get; set; } = 2;
    [JsonPropertyName("accepted")] public PaymentRequirement Accepted { get; set; } = new();
    [JsonPropertyName("payload")] public SignedPayload Payload { get; set; } = new();
}
