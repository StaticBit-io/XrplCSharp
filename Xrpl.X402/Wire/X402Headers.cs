namespace Xrpl.X402.Wire;

/// <summary>HTTP header name constants used by the x402 payment protocol.</summary>
public static class X402Headers
{
    /// <summary>Request/response header carrying the Base64-encoded <see cref="PaymentRequiredChallenge"/> (issued on HTTP 402).</summary>
    public const string PaymentRequired = "PAYMENT-REQUIRED";

    /// <summary>Request header carrying the Base64-encoded <see cref="PaymentSignatureEnvelope"/> (client payment proof).</summary>
    public const string PaymentSignature = "PAYMENT-SIGNATURE";

    /// <summary>Response header carrying the Base64-encoded <see cref="PaymentResponseEnvelope"/> (server settlement result).</summary>
    public const string PaymentResponse = "PAYMENT-RESPONSE";
}
