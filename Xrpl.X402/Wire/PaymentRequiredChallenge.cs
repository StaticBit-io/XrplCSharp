using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

public sealed class PaymentRequiredChallenge
{
    [JsonPropertyName("x402Version")] public int X402Version { get; set; } = 2;
    [JsonPropertyName("accepts")] public List<PaymentRequirement> Accepts { get; set; } = new();
}
