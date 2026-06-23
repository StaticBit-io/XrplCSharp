using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

/// <summary>
/// One entry of the 402 challenge's <c>accepts</c> array (t54 exact scheme).
/// Describes a single payment option the server is willing to accept.
/// </summary>
public sealed class PaymentRequirement
{
    /// <summary>Payment scheme identifier; always <c>"exact"</c> for the t54 XRPL exact scheme.</summary>
    [JsonPropertyName("scheme")] public string Scheme { get; set; } = "exact";

    /// <summary>CAIP-2 network identifier, e.g. <c>"xrpl:1"</c> for XRPL Mainnet.</summary>
    [JsonPropertyName("network")] public string Network { get; set; } = "";

    /// <summary>Asset ticker the payment must be denominated in, e.g. <c>"XRP"</c> or <c>"RLUSD"</c>.</summary>
    [JsonPropertyName("asset")] public string Asset { get; set; } = "";

    /// <summary>Classic XRPL address that receives the payment.</summary>
    [JsonPropertyName("payTo")] public string PayTo { get; set; } = "";

    /// <summary>
    /// Exact amount to pay. For XRP, this is drops as a decimal string; for IOUs, it is the token value string.
    /// </summary>
    [JsonPropertyName("amount")] public string Amount { get; set; } = "";

    /// <summary>
    /// Maximum number of seconds from now within which the signed transaction must be validated.
    /// Maps to an upper bound on <c>LastLedgerSequence</c>.
    /// </summary>
    [JsonPropertyName("maxTimeoutSeconds")] public int MaxTimeoutSeconds { get; set; }

    /// <summary>
    /// Arbitrary extension data supplied by the server, such as <c>invoiceId</c>, <c>sessionId</c>,
    /// <c>sourceTag</c>, and <c>issuer</c> (required for IOU assets).
    /// </summary>
    [JsonPropertyName("extra")] public Dictionary<string, JsonElement> Extra { get; set; } = new();
}
