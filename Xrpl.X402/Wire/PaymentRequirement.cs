using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

public sealed class PaymentRequirement
{
    [JsonPropertyName("scheme")] public string Scheme { get; set; } = "exact";
    [JsonPropertyName("network")] public string Network { get; set; } = "";
    [JsonPropertyName("asset")] public string Asset { get; set; } = "";
    [JsonPropertyName("payTo")] public string PayTo { get; set; } = "";
    [JsonPropertyName("amount")] public string Amount { get; set; } = "";
    [JsonPropertyName("maxTimeoutSeconds")] public int MaxTimeoutSeconds { get; set; }
    [JsonPropertyName("extra")] public Dictionary<string, JsonElement> Extra { get; set; } = new();
}
