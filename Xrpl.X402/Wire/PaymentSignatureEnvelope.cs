using System.Text.Json.Serialization;

namespace Xrpl.X402.Wire;

/// <summary>Holds the signed XRPL transaction blob and the raw invoice id submitted by the client as payment proof.</summary>
public sealed class SignedPayload
{
    /// <summary>Hex-encoded signed transaction blob produced by the XRPL wallet.</summary>
    [JsonPropertyName("signedTxBlob")] public string SignedTxBlob { get; set; } = "";

    /// <summary>Raw invoice id string from <c>extra.invoiceId</c> (any non-empty string). Required by the t54 facilitator.</summary>
    [JsonPropertyName("invoiceId")] public string? InvoiceId { get; set; }
}

/// <summary>
/// The payment proof envelope sent by the client in the <c>PAYMENT-SIGNATURE</c> header (t54 exact scheme).
/// Pairs the chosen <see cref="PaymentRequirement"/> with the corresponding signed transaction.
/// </summary>
public sealed class PaymentSignatureEnvelope
{
    /// <summary>x402 protocol version. Always <c>2</c> for the current specification.</summary>
    [JsonPropertyName("x402Version")] public int X402Version { get; set; } = 2;

    /// <summary>The <see cref="PaymentRequirement"/> entry the client chose to fulfil.</summary>
    [JsonPropertyName("accepted")] public PaymentRequirement Accepted { get; set; } = new();

    /// <summary>Signed transaction payload that satisfies the accepted requirement.</summary>
    [JsonPropertyName("payload")] public SignedPayload Payload { get; set; } = new();

    /// <summary>Optional x402 extensions object (e.g. Verifiable Intent under <c>x402Secure.verifiableIntentChain</c>). Omitted when null.</summary>
    [JsonPropertyName("extensions")]
    public object? Extensions { get; set; }
}
