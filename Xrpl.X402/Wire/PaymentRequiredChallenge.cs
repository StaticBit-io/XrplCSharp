using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

/// <summary>
/// The 402 challenge object returned in the <c>PAYMENT-REQUIRED</c> header (t54 exact scheme).
/// Contains the list of acceptable payment requirements the client may satisfy.
/// </summary>
public sealed class PaymentRequiredChallenge
{
    /// <summary>x402 protocol version. Always <c>2</c> for the current specification.</summary>
    [JsonPropertyName("x402Version")] public int X402Version { get; set; } = 2;

    /// <summary>List of payment options the server accepts; the client picks one to fulfil.</summary>
    [JsonPropertyName("accepts")] public List<PaymentRequirement> Accepts { get; set; } = new();
}
